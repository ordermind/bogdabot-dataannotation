using Bogdabot.Server.Shared.Errors;

namespace Bogdabot.Server.Shared.ValueObjects;

/// <summary>
/// Represents the result of an operation that either succeeds with a value or
/// fails with an <see cref="AbstractError"/>.
///
/// The result can be used in several ways depending on whether the caller needs
/// the value and/or the reason for failure:
///
/// <para>
/// <b>Check success only:</b>
/// <code>
/// if (result)
/// {
///     // Operation succeeded.
/// }
/// </code>
/// </para>
///
/// <para>
/// <b>Retrieve the value without inspecting the error:</b>
/// <code>
/// if (result.Try(out var value))
/// {
///     // Use value.
/// }
/// </code>
/// </para>
///
/// <para>
/// <b>Inspect both success and failure:</b>
/// <code>
/// switch (result)
/// {
///     case Result&lt;T&gt;.Ok(var value):
///         // Operation succeeded.
///         break;
///
///     case Result&lt;T&gt;.Error(var error):
///         // Operation failed; inspect error.
///         break;
/// }
/// </code>
/// </para>
///
/// <para>
/// <b>Construct a successful result:</b>
/// <code>
/// return new Result&lt;T&gt;.Ok(value);
/// </code>
/// </para>
///
/// <para>
/// <b>Construct a failed result:</b>
/// <code>
/// return new Result&lt;T&gt;.Error(new SomeError("Reason"));
/// </code>
/// </para>
/// </summary>
public abstract record Result<T>
{
    public sealed record Ok(T Value) : Result<T>;

    public sealed record Error(AbstractError ErrorValue) : Result<T>;

    public bool IsOk => this is Ok;

    public bool IsError => this is Error;

    public bool Try(out T value)
    {
        if (this is Ok(var result))
        {
            value = result;

            return true;
        }

        value = default!;

        return false;
    }

    public static implicit operator bool(Result<T> result)
    {
        return result is Ok;
    }
}