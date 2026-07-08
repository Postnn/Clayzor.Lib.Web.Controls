namespace Clayzor.Lib.Web.Controls.Components.Grid.ColumnTypes;

/// <summary>
/// Реестр дескрипторов типов колонок. Заменяет <c>MapClrTypeToColumnType</c>.
/// Все дескрипторы — синглтоны без состояния.
/// </summary>
public static class ColumnTypeRegistry
{
    private static readonly TextColumnType    _text    = new();
    private static readonly NumberColumnType  _number  = new();
    private static readonly DecimalColumnType _decimal = new();
    private static readonly BooleanColumnType _boolean = new();
    private static readonly DateColumnType    _date    = new();

    /// <summary>Возвращает дескриптор по CLR-типу свойства сущности.</summary>
    public static ColumnTypeDescriptor FromClr(Type clrType)
    {
        var t = Nullable.GetUnderlyingType(clrType) ?? clrType;
        if (t == typeof(bool))               return _boolean;
        if (t == typeof(DateTime)            ||
            t == typeof(DateTimeOffset)      ||
            t == typeof(DateOnly))           return _date;
        if (t == typeof(decimal)             ||
            t == typeof(double)              ||
            t == typeof(float))              return _decimal;
        if (t == typeof(int)    || t == typeof(long)    ||
            t == typeof(short)  || t == typeof(byte)    ||
            t == typeof(uint)   || t == typeof(ulong)   ||
            t == typeof(ushort))             return _number;
        return _text;
    }

    /// <summary>Возвращает дескриптор по виду колонки.</summary>
    public static ColumnTypeDescriptor FromKind(ColumnType kind) => kind switch
    {
        ColumnType.Number  => _number,
        ColumnType.Decimal => _decimal,
        ColumnType.Date    => _date,
        ColumnType.Boolean => _boolean,
        _                  => _text,
    };
}
