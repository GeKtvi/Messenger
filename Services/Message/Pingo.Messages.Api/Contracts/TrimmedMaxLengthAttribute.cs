using System.ComponentModel.DataAnnotations;

namespace Pingo.Messages.Api.Contracts;

// Like [MaxLength], but ignores surrounding whitespace, which the service trims before storing.
// Derives from MaxLengthAttribute so the OpenAPI schema still shows the limit.
public class TrimmedMaxLengthAttribute(int length) : MaxLengthAttribute(length)
{
    public override bool IsValid(object? value) =>
        base.IsValid(value is string text ? text.Trim() : value);
}
