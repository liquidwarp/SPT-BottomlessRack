using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.DI.Routing;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Utils;

namespace Liquidwarp.UnlockGearProgression.Server;

public record SpawnRequest : BaseInteractionRequestData
{
    [JsonPropertyName("sequence")] public long Sequence { get; set; }
    [JsonPropertyName("lines")] public SpawnLine[]? Lines { get; set; }
}

[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public class CatalogueRouter(JsonUtil json, HttpResponseUtil http, GearService gear) : StaticRouter(json,
[
    new RouteAction<EmptyRequestData>(GearProtocol.Catalogue, async (_, _, id, _, ct) => http.GetBody(await gear.Read(id, true, ct))),
    new RouteAction<EmptyRequestData>(GearProtocol.Receipt, async (_, _, id, _, ct) => http.GetBody(await gear.Read(id, false, ct)))
]);

[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public class SpawnRouter(GearService gear) : ItemEventRouter(
[
    new ItemRouteAction<SpawnRequest>(GearProtocol.Action, (_, pmc, request, id, output, ct) => gear.Spawn(id, pmc, request, output, ct))
]);
