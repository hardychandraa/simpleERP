using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;

namespace SimpleERP.Web.Services;

/// <summary>
/// Parses posted decimals with the invariant culture instead of the server's culture.
///
/// Every money and quantity field in this app is an &lt;input type="number"&gt;, and the
/// HTML spec requires those to submit a "valid floating-point number" — always a dot
/// decimal separator, never a group separator. The default binder parses with
/// CultureInfo.CurrentCulture, which on this machine treats a dot as a *thousands*
/// separator: "3800000.0000" bound as 38,000,000,000 — a four-orders-of-magnitude
/// silent corruption on a payment amount, not an exception.
///
/// Display formatting is untouched: N0 and friends still use the server culture, so
/// amounts keep rendering as 3.800.000. This only changes how posted values are read.
/// </summary>
public class InvariantDecimalModelBinder : IModelBinder
{
    private readonly IModelBinder _fallback;
    public InvariantDecimalModelBinder(IModelBinder fallback) => _fallback = fallback;

    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var value = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (value == ValueProviderResult.None) return _fallback.BindModelAsync(bindingContext);

        var raw = value.FirstValue;
        if (string.IsNullOrWhiteSpace(raw))
        {
            // Empty is "not supplied": let a nullable stay null, and a non-nullable take
            // its default, rather than failing validation on a blank optional field.
            bindingContext.ModelState.SetModelValue(bindingContext.ModelName, value);
            if (Nullable.GetUnderlyingType(bindingContext.ModelType) != null)
                bindingContext.Result = ModelBindingResult.Success(null);
            else
                bindingContext.Result = ModelBindingResult.Success(0m);
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, value);

        if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            bindingContext.Result = ModelBindingResult.Success(parsed);
            return Task.CompletedTask;
        }

        bindingContext.ModelState.TryAddModelError(bindingContext.ModelName,
            $"'{raw}' is not a valid number.");
        return Task.CompletedTask;
    }
}

/// <summary>Applies <see cref="InvariantDecimalModelBinder"/> to decimal and decimal?.</summary>
public class InvariantDecimalModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        var type = context.Metadata.UnderlyingOrModelType;
        if (type != typeof(decimal)) return null;

        return new InvariantDecimalModelBinder(
            new SimpleTypeModelBinder(type, context.Services.GetRequiredService<ILoggerFactory>()));
    }
}
