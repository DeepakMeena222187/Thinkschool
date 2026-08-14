using System.ComponentModel.DataAnnotations;
using System.Text;

namespace QuotesApi.Options;

public sealed class MinimumByteLengthAttribute(int minimumBytes) : ValidationAttribute
{
    public override bool IsValid(object? value)
        => value is string s && Encoding.UTF8.GetByteCount(s) >= minimumBytes;
}
