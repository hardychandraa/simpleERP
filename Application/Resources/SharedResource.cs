namespace SimpleERP.Application.Resources;

/// <summary>
/// Empty marker type. Its only job is to anchor resource lookup: an
/// <c>IStringLocalizer&lt;SharedResource&gt;</c> resolves against the .resx files sitting
/// beside this file, whose manifest names derive from this type's full name
/// (<c>SimpleERP.Application.Resources.SharedResource</c>).
///
/// One shared resource set rather than per-page/per-service ones, deliberately: the same
/// phrases ("Save", "Cancel", "not found") recur across dozens of pages and services, and
/// splitting them per file would mean translating the same word many times and letting the
/// translations drift apart.
///
/// It lives in Application rather than Web because both Web (Razor pages) and
/// Application (service validation messages, Phase B) need to reference it, and
/// Application is the innermost layer both already depend on — putting it in Web would
/// force an outward reference that Clean Architecture forbids here.
///
/// **Keys are the English text itself**, not symbolic identifiers. ASP.NET Core's
/// localizer returns the key unchanged when no translation exists for the active culture,
/// so an untranslated string renders as correct English rather than blank or as a raw
/// key name. That is what lets the translation roll out in phases without any
/// intermediate state looking broken. Enum display names are the one exception — see
/// SharedResource.resx.
/// </summary>
public class SharedResource
{
}
