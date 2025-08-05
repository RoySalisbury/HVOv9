namespace HVO;

public static class OneOfExtensions
{
    /// <summary>
    /// Checks if the OneOf contains a value of type T.
    /// </summary>
    public static bool Is<T>(this IOneOf oneOf) =>
        oneOf.Value is T;

    /// <summary>
    /// Attempts to get the value of type T from the OneOf.
    /// </summary>
    public static bool TryGet<T>(this IOneOf oneOf, out T value)
    {
        if (oneOf.Value is T typed)
        {
            value = typed;
            return true;
        }
        value = default!;
        return false;
    }

    /// <summary>
    /// Gets the value as T or throws an InvalidCastException.
    /// </summary>
    public static T As<T>(this IOneOf oneOf) =>
        (T)oneOf.Value!;
}

