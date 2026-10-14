using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Collaboration.WorkTracking {

  /// <summary>
  /// Stateless, serializer-neutral conversion of arbitrary values (canonical JSON values from a
  /// <see cref="SmartExtensionBag"/>, serializer DOM types, CLR values) into the type a CONSUMER requests.
  /// This is where rich CLR types (Guid, DateTime, enums, Int32, POCOs, ...) are reconstructed - lazily,
  /// driven by the generic argument of the requesting code (requirement R6 of <see cref="SmartExtensionBag"/>).
  /// </summary>
  /// <remarks>
  /// <para><b>WHY CONVERSION HAPPENS ON READ AND NOT ON WRITE</b></para>
  /// <para>
  /// JSON carries six kinds only (string, number, bool, null, object, array). Whether a number was an Int32 or an
  /// Int64, or whether a string was a Guid, cannot be known by ANY deserializer. Guessing is therefore never done
  /// on write; the requesting code is the only party that knows the intended type. Consequently this converter
  /// must be generous on INPUT (accept every reasonable representation) and strict on SEMANTICS (never silently
  /// round, truncate or reinterpret).
  /// </para>
  ///
  /// <para><b>RESOLUTION CHAIN</b> (first matching step wins)</para>
  /// <list type="number">
  ///   <item><term>Null / Nullable</term><description>Nullable&lt;T&gt; is unwrapped. JSON null yields null for
  ///     reference and nullable types and THROWS for non-nullable value types (the serializers behave the same;
  ///     returning default(T) would hide a contract violation, R8).</description></item>
  ///   <item><term>Already assignable</term><description>The value is returned as-is (no copy). This includes
  ///     requesting <c>object</c>, which returns the canonical, live instance stored in the bag.</description></item>
  ///   <item><term>Canonicalization</term><description>The value is normalized (SmartExtensionBag.Normalize), so
  ///     JToken/JsonElement/JsonNode never reach the later steps. A custom POCO is turned into its JSON shape
  ///     (dictionary/list/scalar) so that POCO -&gt; other type conversions follow the same rules as everything else.</description></item>
  ///   <item><term>Object -&gt; target</term><description>string: compact JSON text. Dictionary types
  ///     (IDictionary&lt;K,V&gt;, IReadOnlyDictionary&lt;K,V&gt;, concrete dictionaries): built entry by entry, keys and
  ///     values recursively through this chain. Anything else: POCO via the fallback serializer.</description></item>
  ///   <item><term>Array -&gt; target</term><description>string: compact JSON text. T[], List&lt;T&gt;, the common
  ///     collection interfaces, ISet&lt;T&gt; and concrete ICollection&lt;T&gt; types with a parameterless constructor:
  ///     built element by element through this chain. Anything else: fallback serializer.</description></item>
  ///   <item><term>Scalar -&gt; string</term><description>Invariant text (JSON literals for bool).</description></item>
  ///   <item><term>Scalar -&gt; enum</term><description>BEFORE the generic Convert step, because
  ///     Convert.ChangeType cannot produce enums. Strings: [EnumMember]/[JsonStringEnumMemberName] aliases,
  ///     exact member name, case-insensitive member name, flags ("A, B") and numeric text. Numbers: only integral
  ///     values. Accepting both covers serializers configured with and without string-enum converters.</description></item>
  ///   <item><term>Scalar -&gt; DateTime / DateTimeOffset</term><description>Special-cased before the generic
  ///     TryParse step, because the generic overload would lose DateTimeStyles.RoundtripKind (DateTime.Kind).
  ///     Numbers are NOT interpreted as timestamps: seconds vs. milliseconds cannot be decided reliably.</description></item>
  ///   <item><term>Scalar -&gt; numeric primitives</term><description>Convert.ChangeType with InvariantCulture.
  ///     Numeric text is parsed as decimal first (keeps "1e3" and "1.0" working for integers). A fractional value
  ///     requested as an integer type THROWS instead of rounding (Convert.ToInt32(1.5) would silently yield 2).
  ///     Overflow throws (OverflowException). Booleans are never interpreted as numbers.</description></item>
  ///   <item><term>Scalar -&gt; bool / char</term><description>bool from "true"/"false" text (any casing) only;
  ///     numbers are never interpreted as booleans. char from a one-character string.</description></item>
  ///   <item><term>String -&gt; static TryParse</term><description>Public static
  ///     TryParse(string, IFormatProvider, out T) (the IParsable&lt;T&gt; shape, called with InvariantCulture),
  ///     otherwise TryParse(string, out T). Covers Guid (all formats: D, N, B, P, X), TimeSpan, Version,
  ///     IPAddress, DateOnly/TimeOnly and custom value types following the BCL convention. The MethodInfo is
  ///     cached per target type.</description></item>
  ///   <item><term>TypeConverter</term><description>TypeDescriptor.GetConverter(target).CanConvertFrom(source),
  ///     then the source type's converter CanConvertTo(target). Deliberately AFTER TryParse: TypeDescriptor
  ///     provides default converters for many types even without a [TypeConverter] attribute and is less strict
  ///     about culture; TryParse with InvariantCulture is the more precise contract.</description></item>
  ///   <item><term>Fallback serializer</term><description>Newtonsoft.Json with a PRIVATE, hardened setting set
  ///     (never the global JsonConvert.DefaultSettings): TypeNameHandling.None, metadata properties ignored,
  ///     no date/float guessing. Handles POCOs and types with [JsonConverter] attributes.</description></item>
  ///   <item><term>Failure</term><description>An <see cref="InvalidCastException"/> naming field, source type and
  ///     target type, with the original exception as InnerException. The VALUE itself is not part of the message
  ///     (it may contain personal or confidential data that would end up in logs).</description></item>
  /// </list>
  /// </remarks>
  internal static class SmartValueConverter {

    #region " Fields "

    /// <summary>Cached TryParse delegates per target type (null = type has no usable TryParse).</summary>
    private static readonly ConcurrentDictionary<Type, Func<string, object>> _ParserCache = new ConcurrentDictionary<Type, Func<string, object>>();

    /// <summary>Cached alias names ([EnumMember]/[JsonStringEnumMemberName]) per enum type, case-insensitive.</summary>
    private static readonly ConcurrentDictionary<Type, Dictionary<string, object>> _EnumAliasCache = new ConcurrentDictionary<Type, Dictionary<string, object>>();

    /// <summary>
    /// The fallback serializer. PRIVATE settings on purpose: the global JsonConvert.DefaultSettings of the
    /// host application must not influence (or weaken) the conversion. TypeNameHandling.None and ignored
    /// metadata properties ("$type") make it impossible for payload data to choose CLR types (D11).
    /// </summary>
    private static readonly JsonSerializer _FallbackSerializer = JsonSerializer.Create(new JsonSerializerSettings {
      TypeNameHandling = TypeNameHandling.None,
      MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
      DateParseHandling = DateParseHandling.None,
      FloatParseHandling = FloatParseHandling.Decimal,
      MaxDepth = SmartExtensionBag.MaxNestingDepth
    });

    #endregion

    #region " Entry Points "

    /// <summary>Converts <paramref name="value"/> into <typeparamref name="TTarget"/> (see the resolution chain in the remarks).</summary>
    /// <param name="value">Any value: canonical bag value, CLR value, JToken, JsonElement, POCO, null.</param>
    /// <param name="fieldName">Only used for error messages.</param>
    /// <exception cref="InvalidCastException">The value cannot be represented as <typeparamref name="TTarget"/>.</exception>
    public static TTarget ConvertTo<TTarget>(object value, string fieldName = null) {
      return (TTarget)ConvertTo(value, typeof(TTarget), fieldName);
    }

    /// <summary>Converts <paramref name="value"/> into <paramref name="targetType"/> (see the resolution chain in the remarks).</summary>
    /// <param name="value">Any value: canonical bag value, CLR value, JToken, JsonElement, POCO, null.</param>
    /// <param name="targetType">The type requested by the consumer.</param>
    /// <param name="fieldName">Only used for error messages.</param>
    /// <exception cref="InvalidCastException">The value cannot be represented as <paramref name="targetType"/>.</exception>
    public static object ConvertTo(object value, Type targetType, string fieldName = null) {
      if (targetType == null) {
        throw new ArgumentNullException(nameof(targetType));
      }
      try {
        return ConvertCore(value, targetType, 0);
      }
      catch (Exception ex) when (!(ex is OutOfMemoryException) && !(ex is StackOverflowException)) {
        // exactly ONE wrap at the outermost level -> one consistent message with field context (R8)
        string sourceTypeName = value == null ? "null" : value.GetType().FullName;
        throw new InvalidCastException(
          $"Field '{fieldName ?? "(unnamed)"}': a value of type '{sourceTypeName}' cannot be converted to '{targetType.FullName}'. {ex.Message}",
          ex
        );
      }
    }

    #endregion

    #region " Core Chain "

    private static object ConvertCore(object value, Type targetType, int depth) {
      if (depth > SmartExtensionBag.MaxNestingDepth) {
        throw new InvalidOperationException($"The maximum nesting depth of {SmartExtensionBag.MaxNestingDepth} was exceeded.");
      }

      // (1) 'object' requested -> the stored/given instance itself (the live canonical value, D6)
      if (targetType == typeof(object)) {
        return value;
      }

      // (2) null / Nullable<T>
      Type nullableUnderlying = Nullable.GetUnderlyingType(targetType);
      if (value == null) {
        return NullFor(targetType, nullableUnderlying);
      }
      Type effectiveType = nullableUnderlying ?? targetType; //a boxed Nullable<T> IS a boxed T

      // (3) already assignable -> no conversion, no copy
      if (effectiveType.IsInstanceOfType(value)) {
        return value;
      }

      // (4) canonicalize: no serializer DOM types beyond this point; POCOs become their JSON shape
      object canonical = SmartExtensionBag.Normalize(value);
      if (canonical != null && !IsCanonical(canonical)) {
        canonical = SmartExtensionBag.Normalize(JToken.FromObject(canonical, _FallbackSerializer));
      }
      if (canonical == null) {
        return NullFor(targetType, nullableUnderlying); //e.g. a JValue/JsonElement holding JSON null
      }
      if (effectiveType.IsInstanceOfType(canonical)) {
        return canonical;
      }

      // (5) dispatch by JSON kind
      Dictionary<string, object> map = canonical as Dictionary<string, object>;
      if (map != null) {
        return FromObject(map, effectiveType, depth);
      }
      List<object> list = canonical as List<object>;
      if (list != null) {
        return FromArray(list, effectiveType, depth);
      }
      return FromScalar(canonical, effectiveType);
    }

    private static object NullFor(Type targetType, Type nullableUnderlying) {
      if (!targetType.IsValueType || nullableUnderlying != null) {
        return null;
      }
      throw new InvalidCastException($"JSON null cannot be assigned to the non-nullable value type '{targetType.FullName}'.");
    }

    /// <summary>The value kinds produced by SmartExtensionBag.Normalize, excluding POCOs.</summary>
    private static bool IsCanonical(object value) {
      return value is string || value is bool || value is long || value is decimal || value is double
        || value is Dictionary<string, object> || value is List<object>;
    }

    #endregion

    #region " JSON Object -> Target "

    private static object FromObject(Dictionary<string, object> map, Type targetType, int depth) {

      if (targetType == typeof(string)) {
        return ToJsonText(map);
      }

      Type keyType;
      Type valueType;
      object dictionary = CreateDictionary(targetType, out keyType, out valueType);
      if (dictionary != null) {
        //write through the generic interface indexer: works for every IDictionary<K,V> implementation
        PropertyInfo indexer = typeof(IDictionary<,>).MakeGenericType(keyType, valueType).GetProperty("Item");
        foreach (KeyValuePair<string, object> entry in map) {
          object key = keyType == typeof(string) ? entry.Key : ConvertCore(entry.Key, keyType, depth + 1);
          object item = ConvertCore(entry.Value, valueType, depth + 1);
          indexer.SetValue(dictionary, item, new object[] { key });
        }
        return dictionary;
      }

      //POCO (or anything with a [JsonConverter])
      return ViaFallbackSerializer(map, targetType);
    }

    /// <summary>
    /// Creates an empty dictionary for the requested type: Dictionary&lt;K,V&gt; for the dictionary interfaces,
    /// otherwise an instance of the concrete type if it implements IDictionary&lt;K,V&gt; and has a public
    /// parameterless constructor. Returns null if the target is no dictionary type.
    /// </summary>
    private static object CreateDictionary(Type targetType, out Type keyType, out Type valueType) {
      keyType = null;
      valueType = null;

      if (targetType.IsInterface && targetType.IsGenericType) {
        Type definition = targetType.GetGenericTypeDefinition();
        if (definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>)) {
          Type[] arguments = targetType.GetGenericArguments();
          keyType = arguments[0];
          valueType = arguments[1];
          //ordinal by default, consistent with the bag (D3)
          return Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(keyType, valueType));
        }
        return null;
      }

      if (targetType.IsAbstract || targetType.GetConstructor(Type.EmptyTypes) == null) {
        return null;
      }
      Type dictionaryInterface = targetType.GetInterfaces().FirstOrDefault(
        (i) => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>)
      );
      if (dictionaryInterface == null) {
        return null;
      }
      Type[] interfaceArguments = dictionaryInterface.GetGenericArguments();
      keyType = interfaceArguments[0];
      valueType = interfaceArguments[1];
      return Activator.CreateInstance(targetType);
    }

    #endregion

    #region " JSON Array -> Target "

    private static object FromArray(List<object> list, Type targetType, int depth) {

      if (targetType == typeof(string)) {
        return ToJsonText(list);
      }

      if (targetType.IsArray && targetType.GetArrayRank() == 1) {
        Type elementType = targetType.GetElementType();
        Array array = Array.CreateInstance(elementType, list.Count);
        for (int i = 0; i < list.Count; i++) {
          array.SetValue(ConvertCore(list[i], elementType, depth + 1), i);
        }
        return array;
      }

      Type collectionElementType;
      object collection = CreateCollection(targetType, out collectionElementType);
      if (collection != null) {
        MethodInfo add = typeof(ICollection<>).MakeGenericType(collectionElementType).GetMethod("Add");
        foreach (object item in list) {
          add.Invoke(collection, new object[] { ConvertCore(item, collectionElementType, depth + 1) });
        }
        return collection;
      }

      //custom collection types with a [JsonConverter] or a constructor the serializer understands
      return ViaFallbackSerializer(list, targetType);
    }

    /// <summary>
    /// Creates an empty collection for the requested type: List&lt;T&gt; for IEnumerable&lt;T&gt;, ICollection&lt;T&gt;,
    /// IList&lt;T&gt;, IReadOnlyCollection&lt;T&gt;, IReadOnlyList&lt;T&gt;; HashSet&lt;T&gt; for ISet&lt;T&gt;; otherwise
    /// an instance of a concrete ICollection&lt;T&gt; type with a public parameterless constructor.
    /// Returns null if the target is no supported collection type.
    /// </summary>
    private static object CreateCollection(Type targetType, out Type elementType) {
      elementType = null;

      if (targetType.IsInterface && targetType.IsGenericType) {
        Type definition = targetType.GetGenericTypeDefinition();
        Type argument = targetType.GetGenericArguments()[0];
        if (definition == typeof(IEnumerable<>) || definition == typeof(ICollection<>) || definition == typeof(IList<>)
          || definition == typeof(IReadOnlyCollection<>) || definition == typeof(IReadOnlyList<>)) {
          elementType = argument;
          return Activator.CreateInstance(typeof(List<>).MakeGenericType(argument));
        }
        if (definition == typeof(ISet<>)) {
          elementType = argument;
          return Activator.CreateInstance(typeof(HashSet<>).MakeGenericType(argument));
        }
        return null;
      }

      if (targetType.IsAbstract || targetType.GetConstructor(Type.EmptyTypes) == null) {
        return null;
      }
      Type collectionInterface = targetType.GetInterfaces().FirstOrDefault(
        (i) => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICollection<>)
      );
      if (collectionInterface == null) {
        return null;
      }
      elementType = collectionInterface.GetGenericArguments()[0];
      return Activator.CreateInstance(targetType);
    }

    #endregion

    #region " JSON Scalar -> Target "

    /// <param name="value">A canonical scalar: string, bool, long, decimal or double.</param>
    /// <param name="targetType">Non-nullable target type (Nullable already unwrapped).</param>
    private static object FromScalar(object value, Type targetType) {

      if (targetType == typeof(string)) {
        return SmartExtensionBag.FormatScalar(value);
      }

      if (targetType.IsEnum) {
        return ToEnum(value, targetType);
      }

      if (targetType == typeof(DateTime)) {
        //RoundtripKind keeps 'Z' -> Utc, offsets -> Local, none -> Unspecified
        return DateTime.Parse(RequireText(value, targetType), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
      }
      if (targetType == typeof(DateTimeOffset)) {
        return DateTimeOffset.Parse(RequireText(value, targetType), CultureInfo.InvariantCulture, DateTimeStyles.None);
      }

      if (IsNumericType(targetType)) {
        return ToNumber(value, targetType);
      }

      if (targetType == typeof(bool)) {
        string boolText = value as string;
        if (boolText != null) {
          return bool.Parse(boolText.Trim()); //accepts true/false in any casing
        }
        throw new InvalidCastException("Numbers are not interpreted as booleans.");
      }

      if (targetType == typeof(char)) {
        string charText = RequireText(value, targetType);
        if (charText.Length != 1) {
          throw new FormatException("A char requires a string of exactly one character.");
        }
        return charText[0];
      }

      string text = value as string;
      if (text != null) {
        Func<string, object> parser = GetParser(targetType);
        if (parser != null) {
          return parser(text);
        }
      }

      TypeConverter targetConverter = TypeDescriptor.GetConverter(targetType);
      if (targetConverter.CanConvertFrom(value.GetType())) {
        return targetConverter.ConvertFrom(null, CultureInfo.InvariantCulture, value);
      }
      TypeConverter sourceConverter = TypeDescriptor.GetConverter(value.GetType());
      if (sourceConverter.CanConvertTo(targetType)) {
        return sourceConverter.ConvertTo(null, CultureInfo.InvariantCulture, value, targetType);
      }

      return ViaFallbackSerializer(value, targetType);
    }

    private static object ToNumber(object value, Type targetType) {
      object numeric = value;

      string text = value as string;
      if (text != null) {
        numeric = ParseNumber(text);
      }
      if (numeric is bool) {
        throw new InvalidCastException("Booleans are not interpreted as numbers.");
      }
      if (IsIntegralType(targetType)) {
        EnsureIntegral(numeric);
      }
      //overflow (e.g. long -> int) throws OverflowException
      return Convert.ChangeType(numeric, targetType, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Numeric text -> decimal (preferred: exact, keeps scale) or double (out of decimal range).
    /// NumberStyles.Float accepts sign, decimal point and exponent, but no thousands separators (not JSON).
    /// </summary>
    private static object ParseNumber(string text) {
      string trimmed = text.Trim();
      decimal decimalValue;
      if (decimal.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out decimalValue)) {
        return decimalValue;
      }
      double doubleValue;
      if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out doubleValue)) {
        return doubleValue;
      }
      throw new FormatException("The text is not a valid number.");
    }

    /// <summary>
    /// Prevents silent rounding: Convert.ToInt32(1.5) would return 2 (banker's rounding).
    /// NaN/Infinity cannot be converted to decimal and throw an OverflowException.
    /// </summary>
    private static void EnsureIntegral(object numeric) {
      decimal exact;
      if (numeric is decimal) {
        exact = (decimal)numeric;
      }
      else if (numeric is double) {
        exact = Convert.ToDecimal((double)numeric);
      }
      else {
        return; //long and other integral types
      }
      if (exact != decimal.Truncate(exact)) {
        throw new InvalidCastException("The value has a fractional part and would be rounded.");
      }
    }

    private static object ToEnum(object value, Type enumType) {

      string text = value as string;
      if (text != null) {
        string trimmed = text.Trim();

        // (a) explicit wire aliases ([EnumMember(Value = "...")] / [JsonStringEnumMemberName("...")])
        object aliased;
        if (GetEnumAliases(enumType).TryGetValue(trimmed, out aliased)) {
          return aliased;
        }
        // (b) exact member name first (enums may contain members differing only by case),
        // (c) then case-insensitive names, flags combinations ("A, B") and numeric text ("2")
        if (Enum.IsDefined(enumType, trimmed)) {
          return Enum.Parse(enumType, trimmed, false);
        }
        return Enum.Parse(enumType, trimmed, true);
      }

      if (value is bool) {
        throw new InvalidCastException("Booleans are not interpreted as enum values.");
      }

      EnsureIntegral(value);
      if (Enum.GetUnderlyingType(enumType) == typeof(ulong)) {
        return Enum.ToObject(enumType, Convert.ToUInt64(value, CultureInfo.InvariantCulture));
      }
      return Enum.ToObject(enumType, Convert.ToInt64(value, CultureInfo.InvariantCulture));
    }

    private static Dictionary<string, object> GetEnumAliases(Type enumType) {
      return _EnumAliasCache.GetOrAdd(enumType, (t) => {
        Dictionary<string, object> aliases = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (FieldInfo field in t.GetFields(BindingFlags.Public | BindingFlags.Static)) {
          IList<CustomAttributeData> attributes = field.GetCustomAttributesData();
          foreach (CustomAttributeData attribute in attributes) {
            string alias = null;
            string attributeName = attribute.AttributeType.FullName;
            if (attributeName == "System.Runtime.Serialization.EnumMemberAttribute") {
              foreach (CustomAttributeNamedArgument namedArgument in attribute.NamedArguments) {
                if (namedArgument.MemberName == "Value") {
                  alias = namedArgument.TypedValue.Value as string;
                }
              }
            }
            else if (attributeName == "System.Text.Json.Serialization.JsonStringEnumMemberNameAttribute") {
              if (attribute.ConstructorArguments.Count > 0) {
                alias = attribute.ConstructorArguments[0].Value as string;
              }
            }
            if (!string.IsNullOrEmpty(alias) && !aliases.ContainsKey(alias)) {
              aliases[alias] = field.GetValue(null);
            }
          }
        }
        return aliases;
      });
    }

    private static Func<string, object> GetParser(Type targetType) {
      return _ParserCache.GetOrAdd(targetType, BuildParser);
    }

    /// <summary>
    /// Finds the BCL-convention parser of a type: TryParse(string, IFormatProvider, out T) (the shape of
    /// IParsable&lt;T&gt;, preferred because it allows passing InvariantCulture), otherwise TryParse(string, out T).
    /// </summary>
    private static Func<string, object> BuildParser(Type targetType) {
      if (targetType.IsAbstract || targetType.IsGenericTypeDefinition) {
        return null;
      }
      Type byRefTarget = targetType.MakeByRefType();
      BindingFlags flags = BindingFlags.Public | BindingFlags.Static;

      MethodInfo withProvider = targetType.GetMethod(
        "TryParse", flags, null, new Type[] { typeof(string), typeof(IFormatProvider), byRefTarget }, null
      );
      if (withProvider != null && withProvider.ReturnType == typeof(bool)) {
        return (text) => InvokeTryParse(withProvider, new object[] { text, CultureInfo.InvariantCulture, null }, 2, targetType);
      }

      MethodInfo plain = targetType.GetMethod(
        "TryParse", flags, null, new Type[] { typeof(string), byRefTarget }, null
      );
      if (plain != null && plain.ReturnType == typeof(bool)) {
        return (text) => InvokeTryParse(plain, new object[] { text, null }, 1, targetType);
      }

      return null;
    }

    private static object InvokeTryParse(MethodInfo tryParse, object[] arguments, int outIndex, Type targetType) {
      if ((bool)tryParse.Invoke(null, arguments)) {
        return arguments[outIndex];
      }
      throw new FormatException($"'{targetType.FullName}.TryParse' rejected the text.");
    }

    private static string RequireText(object value, Type targetType) {
      string text = value as string;
      if (text == null) {
        throw new InvalidCastException($"'{targetType.Name}' can only be created from a string (numbers are not interpreted as timestamps or codes).");
      }
      return text;
    }

    private static bool IsIntegralType(Type type) {
      return type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)
        || type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte);
    }

    private static bool IsNumericType(Type type) {
      return IsIntegralType(type) || type == typeof(decimal) || type == typeof(double) || type == typeof(float);
    }

    #endregion

    #region " Fallback Serializer "

    /// <summary>
    /// Last resort for POCOs and types with [JsonConverter]: canonical value -> JToken -> target type,
    /// using the private hardened settings (no type names, no metadata, no guessing).
    /// Newtonsoft matches POCO members case-insensitively, consistent with the bag's redirect (D2).
    /// </summary>
    private static object ViaFallbackSerializer(object canonicalValue, Type targetType) {
      JToken token = JToken.FromObject(canonicalValue, _FallbackSerializer);
      object result = token.ToObject(targetType, _FallbackSerializer);
      if (result == null && targetType.IsValueType) {
        throw new InvalidCastException($"The fallback serializer produced null for the value type '{targetType.FullName}'.");
      }
      return result;
    }

    /// <summary>Compact JSON text of a canonical container (requested as string).</summary>
    private static string ToJsonText(object canonicalValue) {
      using (StringWriter writer = new StringWriter(CultureInfo.InvariantCulture)) {
        _FallbackSerializer.Serialize(writer, canonicalValue);
        return writer.ToString();
      }
    }

    #endregion

  }

}
