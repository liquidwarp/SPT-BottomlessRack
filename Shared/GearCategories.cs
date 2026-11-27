using System.Collections.Generic;
#if NET10_0_OR_GREATER
using TemplateId = SPTarkov.Server.Core.Models.Common.MongoId;
#else
using TemplateId = EFT.MongoID;
#endif

namespace Liquidwarp.BottomlessRack;

internal static class GearCategories
{
    internal const int MaxDepth = 64;
    internal const string SaleWarning = "Gear sales are disabled.";

    internal static readonly HashSet<TemplateId> Families =
    [
        "5422acb9af1c889c16000029", // Weapon
        "5447e1d04bdc2dff2f8b4567", // Knife
        "5448fe124bdc2da5018b4567", // Mod
        "5448bc234bdc2d3c308b4569", // Magazine
        "5448e54d4bdc2dcc718b4568", // Armor
        "644120aa86ffbe10ee032b6f", // Armor plate
        "57bef4c42459772e8d35a53b", // Armored equipment
        "5448e5284bdc2dcb718b4567", // Vest
        "5448e53e4bdc2d60728b4567", // Backpack
        "5a341c4086f77401f2541505", // Headwear
        "5645bcb74bdc2ded0b8b4578", // Headphones
        "5a341c4686f77469e155819e", // Face cover
        "5448e5724bdc2ddf718b4568", // Visors
        "5b3f15d486f77432d0509248"  // Armband
    ];

    internal static readonly HashSet<TemplateId> Exclusions =
    [
        "543be6564bdc2df4348b4568", // Throwable weapon
        "65649eb40bf0ed77b8044453", // Built-in inserts
        "5485a8684bdc2da71d8b4567"  // Ammo
    ];
}
