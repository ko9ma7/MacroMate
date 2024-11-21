using MacroMate.Extensions.Dalamaud.Excel;
using MacroMate.Extensions.Dotnet;
using Lumina.Excel.GeneratedSheets;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace MacroMate.Extensions.Dalamud.PlayerLocation;

/**
 * Manages information about the players current location
 *
 * Region/SubArea code adapted from https://github.com/cassandra308/WhereAmIAgain/blob/main/WhereAmIAgain
 */
public unsafe class PlayerLocationManager {
    public ExcelId<ContentFinderCondition>? Content {
        get {
            var territoryType = Env.DataManager.GetExcelSheet<TerritoryType>()!.GetRow(Env.ClientState.TerritoryType);
            return territoryType
                ?.Let(type => new ExcelId<ContentFinderCondition>(type.ContentFinderCondition.Row))
                ?.DefaultIf(cfc => cfc.Id == 0);
        }
    }

    public ExcelId<PlaceName>? SubAreaName => new ExcelId<PlaceName>(TerritoryInfo.Instance()->SubAreaPlaceNameId).DefaultIf(name => name.Id == 0);

    public ExcelId<PlaceName>? RegionName => new ExcelId<PlaceName>(TerritoryInfo.Instance()->AreaPlaceNameId).DefaultIf(name => name.Id == 0);

    public ExcelId<PlaceName>? TerritoryName {
        get {
            var territoryType = Env.DataManager.GetExcelSheet<TerritoryType>()!.GetRow(Env.ClientState.TerritoryType);
            return territoryType?.Let(type => new ExcelId<PlaceName>(type.PlaceName.Row));
        }
    }
}
