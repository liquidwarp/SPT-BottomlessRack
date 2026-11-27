using System.Reflection;
using System.Text.Json;
using Path = System.IO.Path;
using NUnit.Framework;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Callbacks;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.DI.Routing;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Items;
using SPTarkov.Server.Core.Services.Locales;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using SPTarkov.Server.Core.Utils.Json;
using Liquidwarp.BottomlessRack.Server;

namespace Liquidwarp.BottomlessRack.Tests;

public class TestLog : DispatchProxy
{
    public static ISptLogger<T> For<T>() => Create<ISptLogger<T>, TestLog>();
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name == "Error") TestContext.Progress.WriteLine(args![0]);
        return method.ReturnType == typeof(bool) ? false : null;
    }
}

[NonParallelizable]
public abstract class ServerFixture
{
    protected sealed class TestRouter(params ItemRouteAction[] routes) : ItemEventRouter(routes);

    protected const string Magazine = "564ca99c4bdc2d16268b4589";
    protected const string Rifle = "5644bd2b4bdc2d3b4c8b4572";
    protected const string Optic = "570fd79bd2720bc7458b4583";
    protected const string Ammo = "56dff061d2720bb5668b4567";
    protected const string StashTpl = "566abbc34bdc2d92178b4576";

    protected readonly JsonUtil Json = new([new SptJsonConverterRegistrator()]);
    protected GearDefinitions Gear = null!;
    protected TemplateTable Templates = null!;
    protected GlobalTable Globals = null!;
    protected GearService Service = null!;
    protected SaveServer Saves = null!;
    protected MongoId Session;
    protected SptProfile Profile = null!;
    protected TradersTable TradersDb = null!;
    protected ProfileHelper Profiles = null!;
    protected EventOutputHolder Outputs = null!;
    protected ItemHelper ItemHelper = null!;
    protected PmcData Pmc => Profile.CharacterData!.PmcData!;
    protected BotBaseInventory Inventory => Pmc.Inventory!;
    protected List<Item> Items => Inventory.Items!;
    private readonly string _originalDirectory = Environment.CurrentDirectory;
    private string _testDirectory = null!;
    private CoreConfig _core = null!;
    protected InventoryConfig InventoryConfig = null!;
    protected ServerLocalisationService Localizer = null!;
    protected InventoryHelper Placement = null!;
    protected HttpResponseUtil Http = null!;
    protected readonly ICloner Cloner = new SPTarkov.Server.Core.Utils.Cloners.FastCloner();

    [OneTimeSetUp]
    public void OpenDatabase()
    {
        var install = Environment.GetEnvironmentVariable("BOTTOMLESSRACK_TEST_SPT_PATH")
            ?? throw new InvalidOperationException("Run the suite with build.ps1 -Test.");
        var data = Path.Combine(install, "SPT_Runtime", "SPT_Data");
        _testDirectory = Directory.CreateTempSubdirectory("BottomlessRackTests-").FullName;
        Directory.CreateDirectory(Path.Combine(_testDirectory, "user", "profiles"));
        var locales = Directory.CreateDirectory(Path.Combine(_testDirectory, "SPT_Data/database/locales/server"));
        File.Copy(Path.Combine(data, "database/locales/server/en.json"), Path.Combine(locales.FullName, "en.json"));
        Environment.CurrentDirectory = _testDirectory;
        var items = Json.DeserializeFromFile<Dictionary<MongoId, TemplateItem>>(Path.Combine(data, "database/templates/items.json"))!;
        Templates = new() { Items = items, Character = [], CustomisationStorage = [], Prestige = null!, Quests = [],
            RepeatableQuests = null!, Handbook = null!, Customization = [], Dialogue = null!, Profiles = [], Prices = [],
            DefaultEquipmentPresets = [], Achievements = [], CustomAchievements = [], LocationServices = null! };
        Globals = Json.DeserializeFromFile<GlobalTable>(Path.Combine(data, "database/globals.json"))!;
        _core = Json.DeserializeFromFile<CoreConfig>(Path.Combine(data, "configs/core.json"))!;
        InventoryConfig = Json.DeserializeFromFile<InventoryConfig>(Path.Combine(data, "configs/inventory.json"))!;
        var localeConfig = Json.DeserializeFromFile<LocaleConfig>(Path.Combine(data, "configs/locale.json"))!;
        var locale = new LocaleService(TestLog.For<LocaleService>(), null!, localeConfig);
        Localizer = new ServerLocalisationService(TestLog.For<ServerLocalisationService>(), null!, locale, Json, new());
        var bases = new ItemBaseClassService(TestLog.For<ItemBaseClassService>(), Templates, Localizer);
        bases.HydrateItemBaseClassCache();
        ItemHelper = new(TestLog.For<ItemHelper>(), Templates, null!, null!, bases, null!, Localizer, locale, Cloner);
        Http = new(Json, Localizer);
    }

    [OneTimeTearDown]
    public void CloseDatabase()
    {
        Environment.CurrentDirectory = _originalDirectory;
        if (_testDirectory != null) Directory.Delete(_testDirectory, recursive: true);
    }

    [SetUp]
    public async Task NewCharacter()
    {
        Session = new MongoId();
        var stash = NewItem(StashTpl);
        Profile = new() { ProfileInfo = new() { ProfileId = Session }, SptData = new(),
            CharacterData = new() { PmcData = new() { Id = Session, Bonuses = [], TradersInfo = [], Quests = [],
                Info = new() { Level = 1, Experience = 0 }, Skills = new() { Common = [], Mastering = [] }, Hideout = new(),
                Health = new(), MoneyTransferLimitData = new(), InsuredItems = [], TaskConditionCounters = [],
                Inventory = new() { Stash = stash.Id, Equipment = new MongoId(), SortingTable = new MongoId(), Items = [stash] } } } };
        Saves = new(new(), [], Json, new(null!), null!, null!, TestLog.For<SaveServer>(), _core);
        Saves.AddProfile(Profile);
        Profiles = new(TestLog.For<ProfileHelper>(), Templates, Globals, Cloner, Saves, null!, null!, null!, null!);
        Placement = new(TestLog.For<InventoryHelper>(), Http, null!, Profiles, ItemHelper, Localizer, InventoryConfig, Cloner);
        Outputs = new(Profiles, new(), Cloner);
        TradersDb = new();
        await LoadGear();
    }

    protected async Task<GearDefinitions> LoadGear()
    {
        Gear = new(Templates, Globals, TradersDb, ItemHelper);
        await Gear.OnLoadAsync();
        Service = new(Profiles, Gear, Placement, Saves, Http);
        return Gear;
    }

    protected ItemEventCallbacks Callbacks(params ItemEventRouter[] routers) => new(Http, TestLog.For<ItemEventCallbacks>(),
        TestLog.For<FileLogger>(), Json, Profiles, null!, Outputs, routers, Cloner);

    protected static Item NewItem(string tpl, string? parent = null, string? slot = null) =>
        new() { Id = new MongoId(), Template = tpl, ParentId = parent, SlotId = slot };

    protected static Trader Offer(MongoId trader, params string[] outputs)
    {
        var items = outputs.Select(t => NewItem(t, "hideout", "hideout")).ToList();
        foreach (var item in items) item.Upd = new() { StackObjectsCount = 0 };
        return new()
        {
            Base = new() { Id = trader, Currency = CurrencyType.RUB, UnlockedByDefault = true, LoyaltyLevels = [] },
            Assort = new() { Items = items, LoyalLevelItems = items.ToDictionary(i => i.Id, _ => 1),
                BarterScheme = items.ToDictionary(i => i.Id, _ => new List<List<BarterScheme>> { new() { new() { Template = Rifle, Count = 1 } } }) },
            QuestAssort = [], Dialogue = []
        };
    }

    protected void Award(params string[] templates) =>
        UnlockLedger.Read(Profile).Discover(templates.Select(t => new MongoId(t)), Gear).Write(Profile);

    protected Task<ItemEventRouterResponse> Issue(long seq, params (string Template, int Quantity)[] lines) =>
        Issue(new SpawnRequest { Sequence = seq, Lines = lines.Select(p => new SpawnLine { TemplateId = p.Template, Quantity = p.Quantity }).ToArray() });

    protected async Task<ItemEventRouterResponse> Issue(SpawnRequest request)
    {
        var output = new ItemEventRouterResponse { Warnings = [], ProfileChanges = new() { [Session] = new() { Items = new() { NewItems = [] } } } };
        return await new SpawnRouter(Service).HandleItemEvent(GearProtocol.Action, Pmc, request, Session, output);
    }
}
