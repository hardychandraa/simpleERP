namespace SimpleERP.Web.Pages.Shared;

/// <summary>One tile on a hub page (_Hub partial): where it goes and what it is for.</summary>
public record HubTile(string Icon, string Title, string Description, string Href);
/// <summary>A titled row of tiles.</summary>
public record HubGroup(string Title, IReadOnlyList<HubTile> Tiles);

/// <summary>A tab strip for pages that belong together (_Tabs partial); Active is the current one.</summary>
public record PageTab(string Title, string Href, bool Active);
