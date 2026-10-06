using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;

namespace SimpleERP.Web.Services;

/// <summary>
/// Trims leading and trailing whitespace from every posted string (HC, 2026-10-06).
///
/// Done once at binding instead of per service, so every current and future form gets it:
/// a name typed as "ROBIN " no longer becomes a second customer next to "ROBIN", and a
/// search or duplicate check never misses because of a stray space. A value that is only
/// whitespace becomes null, the same as an empty field already does by default.
///
/// Passwords are left exactly as typed: a space can be part of one, and trimming it would
/// lock the user out or silently change what they chose.
/// </summary>
public class TrimmingModelBinder : IModelBinder
{
    private readonly IModelBinder _inner;
    public TrimmingModelBinder(IModelBinder inner) => _inner = inner;

    public async Task BindModelAsync(ModelBindingContext bindingContext)
    {
        await _inner.BindModelAsync(bindingContext);
        if (bindingContext.Result.IsModelSet && bindingContext.Result.Model is string s)
        {
            var trimmed = s.Trim();
            bindingContext.Result = ModelBindingResult.Success(trimmed.Length == 0 ? null : trimmed);
        }
    }
}

/// <summary>Applies <see cref="TrimmingModelBinder"/> to every string except password fields.</summary>
public class TrimmingModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        if (context.Metadata.ModelType != typeof(string)) return null;
        // Every password field in the app is named *Password (Password, NewPassword,
        // ConfirmPassword, CurrentPassword), as a property or a handler parameter.
        if (context.Metadata.Name?.Contains("Password", StringComparison.OrdinalIgnoreCase) == true
            || context.Metadata.DataTypeName == "Password")
            return null;

        return new TrimmingModelBinder(
            new SimpleTypeModelBinder(typeof(string), context.Services.GetRequiredService<ILoggerFactory>()));
    }
}
