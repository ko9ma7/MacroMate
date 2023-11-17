using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Dalamud.Game.Addon.Lifecycle;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.System.Memory;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using FFXIVClientStructs.FFXIV.Component.GUI;
using MacroMate.Extensions.Dotnet;
using MacroMate.Extensions.FFXIVClientStructs;

namespace MacroMate.Extensions.Dalamud.Macros;

public unsafe class VanillaMacroManager : IDisposable {
    private RaptureShellModule* raptureShellModule;
    private RaptureMacroModule* raptureMacroModule;
    private RaptureHotbarModule* raptureHotbarModule;
    private AgentMacro* agentMacro;

    public VanillaMacroManager() {
        raptureShellModule = RaptureShellModule.Instance();
        raptureMacroModule = RaptureMacroModule.Instance();
        raptureHotbarModule = RaptureHotbarModule.Instance();
        agentMacro = XIVCS.GetAgent<AgentMacro>();
    }

    public void Dispose() {}

    public VanillaMacro GetMacro(VanillaMacroSet macroSet, uint macroNumber) {
        if (macroNumber < 0 || macroNumber > 99) {
            throw new ArgumentException($"Invalid macroNumber: {macroNumber}");
        }

        var macro = raptureMacroModule->GetMacro((uint)macroSet, macroNumber);
        var lineCount = raptureMacroModule->GetLineCount(macro);
        var lazyLines = new Lazy<string>(() => {
            var lines = macro->LinesSpan
                .ToArray()
                .Take((int)lineCount)
                .Select(line => line.ToString());
            return string.Join("\n", lines);
        });

        return new VanillaMacro(
            IconId: macro->IconId,
            Title: macro->Name.ToString(),
            LineCount: lineCount,
            Lines: lazyLines,
            IconRowId: macro->MacroIconRowId
        );
    }

    public void ExecuteMacro(VanillaMacro vanillaMacro) {
        var macroText = vanillaMacro.Lines.Value;
        if (macroText.Count(c => c == '\n') > 15) {
            Env.ChatGui.PrintError($"Macro has too many lines (max 15)");
            return;
        }

        var macroPtr = IntPtr.Zero;
        try {
            macroPtr = Marshal.AllocHGlobal(Macro.size);
            var macro = new Macro(macroPtr, string.Empty, macroText.Split("\n"));
            Marshal.StructureToPtr(macro, macroPtr, false);

            raptureShellModule->ExecuteMacro((RaptureMacroModule.Macro*)Unsafe.AsRef(macroPtr));
            macro.Dispose();
        } catch (Exception e) {
            Env.PluginLog.Error($"Failed to execute macro {e}");
            Env.ChatGui.PrintError($"Failed to execute macro");
        }

        Marshal.FreeHGlobal(macroPtr);
    }

    public void SetMacro(VanillaMacroLink link, VanillaMacro vanillaMacro) {
        SetMacro(link.Set, link.Slot, vanillaMacro);
    }

    public void SetMacro(
        VanillaMacroSet macroSet,
        uint macroSlot,
        VanillaMacro vanillaMacro
    ) {
        if (macroSlot < 0 || macroSlot > 99) {
            Env.ChatGui.PrintError($"Invalid macro slot: {macroSlot}");
            return;
        }

        var macroText = vanillaMacro.Lines.Value;

        if (macroText.Count(c => c == '\n') > 15) {
            Env.ChatGui.PrintError($"Macro {macroSlot} has too many lines (max 15)");
            return;
        }

        var macro = raptureMacroModule->GetMacro((uint)macroSet, macroSlot);
        var macroNameUtf8 = new Utf8String();
        macroNameUtf8.SetString(vanillaMacro.Title.Truncate(15));
        macro->Name = macroNameUtf8;
        macro->IconId = vanillaMacro.IconId;
        macro->MacroIconRowId = vanillaMacro.IconRowId;

        var macroTextUtf8 = Utf8String.FromString(macroText);
        raptureMacroModule->ReplaceMacroLines(macro, macroTextUtf8);
        macroTextUtf8->Dtor();
        IMemorySpace.Free(macroTextUtf8);

        raptureMacroModule->SetSavePendingFlag(true, (uint)macroSet);
        raptureMacroModule->UserFileEvent.HasChanges = true;

        // Update the MacroAddon if it's open (since it doesn't auto-refresh)
        SetVanillaMacroUISlotIcon(macroSet, macroSlot, vanillaMacro.IconId);

        Env.XIVCSSignatures.RaptureHotbarModuleReloadMacroSlots!(
            raptureHotbarModule, (byte)macroSet, (byte)macroSlot
        );
    }

    public void DeleteMacro(VanillaMacroSet macroSet, uint macroSlot) {
        if (macroSlot < 0 || macroSlot > 99) {
            Env.ChatGui.PrintError($"Invalid macro slot: {macroSlot}");
            return;
        }

        var deletedMacro = new VanillaMacro(
            IconId: 0,
            Title: "",
            LineCount: 0,
            Lines: new(() => ""),
            IconRowId: 0
        );
        SetMacro(macroSet, macroSlot, deletedMacro);
    }

    public void ShowMacroUI() {
        var ui = (UIModule*)Env.GameGui.GetUIModule();

        var macroAddonAtk  = GetAddonMacro();
        if (macroAddonAtk != null) {
            if (macroAddonAtk->IsVisible) {
                return;
            }
        }

        // Execute "User Macros"
        // See: https://github.com/xivapi/ffxiv-datamining/blob/master/csv/MainCommand.csv
        ui->ExecuteMainCommand(21);
    }

    /// <summary>
    /// Move the Vanilla Macro UIs selection cursor and tab to the given parameters
    /// </summary>
    /// <remarks>
    /// This changes the selection even if the UI isn't currently open.
    /// </remarks>
    public void SelectMacroInUI(VanillaMacroSet macroSet, uint macroSlot) {
        var agentModule = Framework.Instance()->GetUiModule()->GetAgentModule();
        var agentMacro = (AgentMacro*)agentModule->GetAgentByInternalId(AgentId.Macro);
        if (agentMacro == null) { return; }

        agentMacro->SelectedMacroSet = (uint)macroSet;
        agentMacro->SelectedMacroIndex = macroSlot;
        Env.XIVCSSignatures.AgentMacroReloadSelection!(
            agentMacro,
            agentMacro->SelectedMacroIndex,
            false
        );
    }

    /// When we change the Icon of a macro it doesn't actually refresh the Macro UI when it's open,
    /// so we need to write the UI values ourself.
    private void SetVanillaMacroUISlotIcon(VanillaMacroSet macroSet, uint macroSlot, uint iconId) {
        var addonMacro = GetAddonMacro();
        if (addonMacro == null) { return; }

        // We only want to update the screen if it's visible, since otherwise we'll be writing
        // shared icons into individual and vice-versa.
        var addonMacroSet = (VanillaMacroSet)addonMacro->AtkValues[05].Int; // Individual / Shared
        if (macroSet == addonMacroSet) {
            // This sets the active selection of the macro window. It's a bit of a hack but
            // I couldn't see any other way to "target" the slot.
            addonMacro->AtkValues[04].Int = (int)macroSlot;

            // This sets the icon of the active selection
            addonMacro->AtkValues[12].Int = (int)iconId;

            addonMacro->OnRefresh(addonMacro->AtkValuesCount, addonMacro->AtkValues);
        }
    }

    private AtkUnitBase* GetAddonMacro() {
        var macroAddonRaw = Env.GameGui.GetAddonByName("Macro");
        if (macroAddonRaw == nint.Zero) { return null; }
        var macroAddon = (AtkUnitBase*)macroAddonRaw;

        // We need to make sure the addon is fully initialised (i.e. Setup has finished) before
        // calling OnRefresh, otherwise things crash.
        if (macroAddon->RootNode == null) { return null; }
        if (macroAddon->RootNode->ChildNode == null) { return null; }

        return macroAddon;
    }
}
