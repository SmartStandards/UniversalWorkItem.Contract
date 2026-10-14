using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Collaboration.WorkTracking {

  /// <summary>
  /// Extension-data container ("bag") for open, extensible contracts.
  /// It holds every JSON member of an incoming payload that is NOT covered by an explicit (well-known)
  /// property of its owner, and the serializers flatten it back onto the root level of the owner's JSON object.
  /// It is plugged into a contract class through ONE property that is annotated with
  /// <c>[JsonExtensionData]</c> for Newtonsoft.Json AND System.Text.Json, so it works with both serializers
  /// without any configuration on the consumer side.
  /// </summary>
  /// <remarks>
  /// <para><b>READ THIS BEFORE PROPOSING A "SIMPLER" ALTERNATIVE</b></para>
  /// <para>
  /// This class is the result of a deliberate architecture decision. Every obvious alternative
  /// (ExpandoObject, the owner implementing IDictionary itself, custom converters/resolvers, raw JsonElement
  /// storage, type hints in the payload, ...) was evaluated and rejected against the full set of requirements
  /// listed below. An alternative that optimizes one requirement but violates another is NOT an improvement.
  /// Check the requirements R1-R8 and the decisions D1-D12 first.
  /// </para>
  ///
  /// <para><b>REQUIREMENTS</b></para>
  /// <list type="table">
  ///   <item><term>R1 Contract-first</term><description>
  ///     Well-known fields are typed CLR properties. The contract must stay fully visible and precise in
  ///     OpenAPI/Swagger and UJMW declarations. An untyped "object map" contract is not acceptable.
  ///   </description></item>
  ///   <item><term>R2 Open contract</term><description>
  ///     Payload members unknown to the contract (e.g. Trello custom fields, Azure DevOps schema extensions)
  ///     must survive a deserialize/serialize roundtrip and must be usable by code that knows about them.
  ///   </description></item>
  ///   <item><term>R3 Zero configuration</term><description>
  ///     Must work out-of-the-box with the DEFAULT settings of System.Text.Json and Newtonsoft.Json.
  ///     Consumers must never be forced to register converters, contract resolvers, type-info modifiers
  ///     or to know about this class at all.
  ///   </description></item>
  ///   <item><term>R4 Serializer neutrality</term><description>
  ///     Data deserialized by one serializer must be serializable by the other one (mixed pipelines, e.g. an
  ///     STJ-based client library feeding a Newtonsoft-based web service). No serializer-specific DOM types
  ///     (JsonElement, JObject, JsonNode, ...) may ever be stored.
  ///   </description></item>
  ///   <item><term>R5 Transport never mutates data</term><description>
  ///     Key casing is never changed and no naming policy is ever applied to extension keys. Dictionary keys
  ///     can originate from other layers; rewriting them destroys data.
  ///   </description></item>
  ///   <item><term>R6 The consumer decides the type</term><description>
  ///     JSON cannot carry CLR types. Only the party requesting a value (via a generic argument, or an adapter
  ///     via its field schema) knows the target type. Typing is therefore lazy and happens on read.
  ///   </description></item>
  ///   <item><term>R7 Minimal dependencies</term><description>
  ///     BCL + Newtonsoft.Json (de-facto part of our component landscape). System.Text.Json is part of the
  ///     framework since .NET Core 3.0 and is only referenced inside conditional compilation blocks.
  ///   </description></item>
  ///   <item><term>R8 Fail loud</term><description>
  ///     No silent data loss. Conversion problems surface as exceptions naming the field and the types.
  ///   </description></item>
  /// </list>
  ///
  /// <para><b>DESIGN DECISIONS</b></para>
  /// <list type="number">
  ///   <item><term>D1 A dedicated bag property instead of the contract class being a dictionary</term><description>
  ///     <c>[JsonExtensionData]</c> is the only mechanism BOTH serializers support natively without configuration
  ///     (R3), so it is applied twice (Newtonsoft + STJ) to one property of the owner. This metadata-only coupling
  ///     is the single accepted dependency on serializer-specific types.
  ///     REJECTED (a) "dictionary hijack" (the owner itself implements IDictionary&lt;string, object&gt;): every
  ///     serializer then treats the whole class as a map, so OpenAPI shows an untyped dictionary (violates R1),
  ///     attributes/converters/options on well-known properties are ignored, and the class needs its own
  ///     mini-serializer to rebuild nested well-known properties (e.g. LinkedItem[] arriving as JObject/JsonElement).
  ///     REJECTED (b) ExpandoObject / DynamicObject: no typed well-known properties (R1), same typing problem.
  ///     REJECTED (c) custom JsonConverter / ContractResolver / STJ type-info modifier: requires consumer
  ///     configuration (R3).
  ///     REJECTED (d) a naming convention ("serializers should recognize a property called Extensions"):
  ///     no serializer follows it, so it still requires configuration (R3).
  ///   </description></item>
  ///   <item><term>D2 Owner-aware redirect of case variants</term><description>
  ///     Newtonsoft matches properties case-insensitively, STJ (non-web defaults) case-sensitively. With STJ a
  ///     payload member "title" lands in the extension data although the contract defines "Title". The bag
  ///     knows its owner: such a member is converted and written into the real property and is NOT stored in the
  ///     bag. This gives both serializers identical semantics without any consumer setting (R3) and without
  ///     ever rewriting key casing (R5). An exact (ordinal) match wins over a case-insensitive one; an ambiguous
  ///     case-insensitive match throws.
  ///   </description></item>
  ///   <item><term>D3 Ordinal keys inside the bag</term><description>
  ///     "foo" and "Foo" are two different pieces of data (R5) and are stored separately. Case-insensitivity
  ///     is used ONLY for matching against well-known properties (D2) and as a fallback on typed reads
  ///     (exact key first, ambiguous fallback throws).
  ///   </description></item>
  ///   <item><term>D4 Eager normalization on insert</term><description>
  ///     Every value is converted into a canonical JSON representation on the way IN (indexer, Add, Adopt).
  ///     REJECTED: storing raw JsonElement/JToken and converting lazily on read. (a) Newtonsoft cannot serialize
  ///     JsonElement meaningfully and STJ cannot serialize JObject meaningfully, so mixed pipelines break (R4).
  ///     (b) Converting on every read hands out fresh copies, so mutating a nested value
  ///     (<c>((IDictionary&lt;string, object&gt;)bag["Meta"])["x"] = 1</c>) would be silently lost (R8). Fixing that
  ///     requires a cache, which is eager normalization with extra steps.
  ///   </description></item>
  ///   <item><term>D5 Only the JSON type system is stored</term><description>
  ///     JSON knows exactly six kinds: string, number, bool, null, object, array. A Guid, DateTime, enum or
  ///     Int32 cannot survive transport as such; every deserializer only sees a string or a number. The bag
  ///     therefore does not pretend otherwise: rich CLR values are stored in their canonical JSON representation
  ///     and rich types are reconstructed ONLY on typed access (R6).
  ///     REJECTED: guessing on insert ("this string looks like a Guid / a date"). Re-formatting (casing, braces,
  ///     offsets) would mutate data (R5), and the guess is pointless because the next serializer turns the value
  ///     back into a string anyway.
  ///     CANONICAL MAPPING: object -&gt; Dictionary&lt;string, object&gt; (ordinal); array -&gt; List&lt;object&gt;;
  ///     integer fitting Int64 -&gt; long; other numbers -&gt; decimal (preserves notation scale, "1.50" stays
  ///     "1.50") or double if out of decimal range; string -&gt; string; bool -&gt; bool; null -&gt; null.
  ///     CLR scalars: Int32/Int16/... -&gt; long, float -&gt; double (shortest roundtrip form), Guid -&gt; "D" string,
  ///     DateTime/DateTimeOffset -&gt; ISO 8601 "o", TimeSpan -&gt; "c", enum -&gt; member name, byte[] -&gt; base64,
  ///     Uri -&gt; original string. Custom POCOs inserted by code are kept by reference (every serializer can
  ///     write them) and are only shaped on typed reads.
  ///   </description></item>
  ///   <item><term>D6 Value semantics on insert</term><description>
  ///     Normalization always rebuilds dictionaries and lists, so inserting a container stores a deep copy.
  ///     Later changes to the caller's instance do not affect the bag and vice versa. Containers read back with
  ///     <c>object</c> or their canonical type ARE the live instances (mutations are kept). POCOs are kept by reference.
  ///   </description></item>
  ///   <item><term>D7 An honest IDictionary contract</term><description>
  ///     Keys, Count, the enumerator, the indexer getter and TryGetValue reflect ONLY the extension entries.
  ///     Well-known properties are never visible through the bag; the redirect (D2) only affects WRITES.
  ///     Unified access across both worlds goes through the owner (GetField/SetField).
  ///     REJECTED: also exposing well-known properties through the bag. Keys/Count/enumerator would disagree
  ///     with the indexer, and serializers would write those keys twice.
  ///   </description></item>
  ///   <item><term>D8 Well-known member semantics mirror the serializers</term><description>
  ///     Properties carrying [JsonIgnore] (Newtonsoft; STJ only with Condition = Always) are not part of the
  ///     wire contract, so such keys stay in the bag. Renamed properties ([JsonProperty("x")],
  ///     [JsonPropertyName("x")]) are matched by the wire names of BOTH serializers. Read-only properties
  ///     (no public setter and no [JsonInclude]/[JsonProperty]) are output-only: incoming values for them are
  ///     discarded, exactly like the serializers do. The [JsonExtensionData] property itself and indexers are
  ///     excluded. A payload member literally called like the bag property (e.g. a nested "Extensions" object
  ///     written by a non-conforming serializer) is plain data and is kept as such; nothing is guessed.
  ///   </description></item>
  ///   <item><term>D9 Last write wins on collisions</term><description>
  ///     If a payload contains both "Title" and "title", the result depends on member order. This is documented
  ///     rather than tracked, because tracking would require hooks in every property setter of the contract.
  ///   </description></item>
  ///   <item><term>D10 Fail loud on redirect</term><description>
  ///     A value redirected to a well-known property that cannot be converted to the property type throws
  ///     an <see cref="InvalidCastException"/> (field name + types). It is neither dropped nor kept in the bag
  ///     (a case-variant copy next to the property would be written back as a second key).
  ///   </description></item>
  ///   <item><term>D11 No type hints in the payload</term><description>
  ///     Type information never travels with the data and Type.GetType is never called with data from the
  ///     payload. The consumer's generic argument (or an adapter's field schema) is the only source of truth.
  ///     This also eliminates the TypeNameHandling class of deserialization vulnerabilities.
  ///   </description></item>
  ///   <item><term>D12 internal</term><description>
  ///     This type is an implementation detail. The public surface is the owner's
  ///     IDictionary&lt;string, object&gt; property; nobody outside the assembly depends on this class.
  ///   </description></item>
  /// </list>
  ///
  /// <para><b>KNOWN AND ACCEPTED LIMITATIONS</b></para>
  /// <list type="bullet">
  ///   <item><description>L1 JSON numbers keep their VALUE, not necessarily their notation ("1e3" -&gt; 1000).
  ///     STJ input keeps the decimal scale; Newtonsoft (FloatParseHandling.Double by default) has already turned
  ///     "1.50" into the double 1.5 before the bag sees it.</description></item>
  ///   <item><description>L2 Newtonsoft's date guessing (DateParseHandling.DateTime by default) happens BEFORE the
  ///     bag sees the value. The bag writes such values back as ISO 8601 "o", which can differ from the original
  ///     notation/offset. Use DateParseHandling.None where exact preservation matters.</description></item>
  ///   <item><description>L3 Numbers outside the double range (STJ input) are kept as their raw JSON text (string).</description></item>
  ///   <item><description>L4 Not thread-safe (same guarantees as <see cref="Dictionary{TKey, TValue}"/>).</description></item>
  ///   <item><description>L5 The case-insensitive fallback on typed reads is a linear scan over the extension
  ///     entries (bags are small; the exact lookup is O(1)).</description></item>
  /// </list>
  ///
  /// <para><b>USAGE PATTERN (the complete effort for any contract class)</b></para>
  /// <code>
  /// public partial class MyContract {
  ///
  ///   [DebuggerBrowsable(DebuggerBrowsableState.Never)]
  ///   private SmartExtensionBag _Extensions = null;
  ///
  ///   [Newtonsoft.Json.JsonExtensionData]
  /// #if NETCOREAPP3_0_OR_GREATER
  ///   [System.Text.Json.Serialization.JsonExtensionData]
  /// #endif
  ///   public IDictionary&lt;string, object&gt; Extensions {
  ///     get {
  ///       if (_Extensions == null) _Extensions = new SmartExtensionBag(this); // 'this' is not allowed in field initializers
  ///       return _Extensions;
  ///     }
  ///     set {
  ///       _Extensions = SmartExtensionBag.Adopt(this, value); // covers serializers assigning their own instance
  ///     }
  ///   }
  ///
  /// }
  /// </code>
  /// </remarks>
  [DebuggerDisplay("Extensions: {Count}")]
  [DebuggerTypeProxy(typeof(SmartExtensionBag.DebugView))]
  internal sealed class SmartExtensionBag : IDictionary<string, object> {

    #region " Fields & Construction "

    /// <summary>
    /// Same limit as the default MaxDepth of both serializers. Protects against cyclic object graphs
    /// inserted by code (normalization walks dictionaries and enumerables recursively).
    /// </summary>
    internal const int MaxNestingDepth = 64;

    /// <summary>
    /// The contract instance owning this bag. Needed for the redirect of case variants (D2) and for
    /// unified reads (well-known property first, then bag).
    /// </summary>
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly object _Owner;

    /// <summary>
    /// Cached reflection metadata of the owner's runtime type (one instance per type, shared by all bags).
    /// The runtime type (not a generic parameter) is used deliberately, so subclasses are matched correctly.
    /// </summary>
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly OwnerContract _OwnerContract;

    /// <summary>
    /// The actual storage. ORDINAL comparer on purpose (D3): keys differing only by case are different data.
    /// Contains canonical values only (D4/D5).
    /// </summary>
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly Dictionary<string, object> _Entries = new Dictionary<string, object>(StringComparer.Ordinal);

    /// <summary>
    /// Creates an empty bag bound to its owner.
    /// </summary>
    /// <param name="owner">
    /// The contract instance whose well-known properties take precedence over bag entries.
    /// Pass <c>this</c> from the owner's lazy property getter (field initializers cannot reference <c>this</c>).
    /// </param>
    public SmartExtensionBag(object owner) {
      if (owner == null) {
        throw new ArgumentNullException(nameof(owner));
      }
      _Owner = owner;
      _OwnerContract = OwnerContract.For(owner.GetType());
    }

    /// <summary>
    /// Implements the SETTER of the owner's extension property.
    /// Serializers normally reuse the (never null) instance returned by the getter, but some code paths, and
    /// regular code (<c>itemB.Extensions = itemA.Extensions</c>), assign a foreign dictionary. Whatever arrives
    /// here ends up as a bag that is bound to the correct owner and contains canonical values only.
    /// </summary>
    /// <param name="owner">The contract instance that will own the resulting bag.</param>
    /// <param name="incoming">The assigned dictionary (may be null, a foreign dictionary, or a bag).</param>
    /// <returns>
    /// <list type="bullet">
    ///   <item><description>the SAME instance if <paramref name="incoming"/> already is a bag of this owner (no copy);</description></item>
    ///   <item><description>an EMPTY bag for <c>null</c> (assignment replaces, like any property assignment);</description></item>
    ///   <item><description>otherwise a NEW bag filled entry by entry through the regular insert path, i.e. with
    ///     redirect to well-known properties (D2) and normalization (D4). A bag of another owner is copied
    ///     as well: its entries may be well-known properties for the new owner's type, and the copy must
    ///     not share mutable containers with the source (D6).</description></item>
    /// </list>
    /// </returns>
    public static SmartExtensionBag Adopt(object owner, IDictionary<string, object> incoming) {
      SmartExtensionBag existingBag = incoming as SmartExtensionBag;
      if (existingBag != null && ReferenceEquals(existingBag._Owner, owner)) {
        return existingBag;
      }
      SmartExtensionBag newBag = new SmartExtensionBag(owner);
      if (incoming != null) {
        foreach (KeyValuePair<string, object> entry in incoming) {
          //last write wins, also for case variants inside the incoming dictionary (D9)
          newBag.Store(entry.Key, entry.Value, throwOnDuplicate: false);
        }
      }
      return newBag;
    }

    #endregion

    #region " Insert Path (Redirect + Normalization) "

    /// <summary>
    /// The single insert path used by the indexer setter, both Add overloads and <see cref="Adopt"/>.
    /// Both serializers fill extension data through the indexer setter, so this is the place where
    /// redirect (D2) and normalization (D4) happen during deserialization.
    /// </summary>
    /// <param name="key">The member name exactly as it appeared in the payload (casing preserved, R5).</param>
    /// <param name="value">Any value: CLR value, JToken, JsonElement, JsonNode, dictionary, list, POCO.</param>
    /// <param name="throwOnDuplicate">IDictionary.Add semantics (true) vs. indexer semantics (false).</param>
    private void Store(string key, object value, bool throwOnDuplicate) {
      if (key == null) {
        throw new ArgumentNullException(nameof(key));
      }

      // (1) Well-known property? -> write the property, never the bag (D2).
      //     TryResolve prefers an exact match and throws on ambiguous case-insensitive matches.
      OwnerContract.KnownMember member;
      if (_OwnerContract.TryResolve(key, out member)) {
        this.AssignToKnownMember(member, value);
        return;
      }

      // (2) Genuine extension member -> store its canonical JSON representation (D4/D5).
      if (throwOnDuplicate && _Entries.ContainsKey(key)) {
        throw new ArgumentException($"An extension entry with the key '{key}' already exists.", nameof(key));
      }
      _Entries[key] = Normalize(value);
    }

    /// <summary>
    /// Writes a value that arrived under a (case-variant) name of a well-known property into that property.
    /// </summary>
    private void AssignToKnownMember(OwnerContract.KnownMember member, object value) {

      // Output-only properties: the serializers ignore incoming values for them, so do we (D8).
      if (!member.IsWritable) {
        return;
      }

      // The value is usually a JsonElement (STJ) or a JToken/CLR value (Newtonsoft) and can be arbitrarily
      // nested (e.g. LinkedItem[]). SmartValueConverter normalizes it and runs the full resolution chain.
      // A failure throws an InvalidCastException naming field and types (D10, R8) - deliberately NOT caught here.
      object converted = SmartValueConverter.ConvertTo(value, member.Property.PropertyType, member.Property.Name);
      member.Property.SetValue(_Owner, converted, null);
    }

    #endregion

    #region " Normalization (canonical JSON representation) "

    /// <summary>
    /// Converts any value into its canonical JSON representation (see D5 for the mapping table).
    /// The result consists ONLY of: null, string, bool, long, decimal, double,
    /// Dictionary&lt;string, object&gt; (ordinal, recursively canonical), List&lt;object&gt; (recursively canonical)
    /// or a custom POCO kept by reference.
    /// </summary>
    /// <remarks>
    /// Containers are always rebuilt, so the result never shares mutable containers with the input (D6).
    /// Serializer-specific DOM types (JToken, JsonElement, JsonDocument, JsonNode) are always resolved
    /// completely, so they can never leak into the bag (R4).
    /// </remarks>
    internal static object Normalize(object value) {
      return NormalizeCore(value, 0);
    }

    private static object NormalizeCore(object value, int depth) {
      EnsureDepth(depth);

      if (value == null || value is DBNull) {
        return null;
      }

      // (A) Already canonical JSON scalars - the hot path.
      if (value is string || value is bool || value is long || value is decimal || value is double) {
        return value;
      }

      // (B) Serializer-specific DOM types. These MUST be handled before the IDictionary/IEnumerable checks
      //     below, because JObject/JArray/JsonObject/JsonArray implement those interfaces with
      //     serializer-specific element types.
      JToken token = value as JToken;
      if (token != null) {
        return NormalizeJToken(token, depth);
      }
#if NETCOREAPP3_0_OR_GREATER
      if (value is System.Text.Json.JsonElement) {
        return NormalizeJsonElement((System.Text.Json.JsonElement)value, depth);
      }
      System.Text.Json.JsonDocument jsonDocument = value as System.Text.Json.JsonDocument;
      if (jsonDocument != null) {
        //the caller owns (and disposes) the document; we copy everything out immediately
        return NormalizeJsonElement(jsonDocument.RootElement, depth);
      }
#endif
#if NET6_0_OR_GREATER
      System.Text.Json.Nodes.JsonNode jsonNode = value as System.Text.Json.Nodes.JsonNode;
      if (jsonNode != null) {
        //JsonNode has no cheap read-only walker that is identical across .NET versions -> go through its JSON text
        using (System.Text.Json.JsonDocument nodeDocument = System.Text.Json.JsonDocument.Parse(jsonNode.ToJsonString())) {
          return NormalizeJsonElement(nodeDocument.RootElement, depth);
        }
      }
#endif

      // (C) Remaining CLR numbers -> long / decimal / double (the JSON "number" kind).
      switch (value) {
        case int i: return (long)i;
        case short s: return (long)s;
        case sbyte sb: return (long)sb;
        case byte b: return (long)b;
        case ushort us: return (long)us;
        case uint ui: return (long)ui;
        case ulong ul: return ul <= (ulong)long.MaxValue ? (object)(long)ul : (object)(decimal)ul;
        //float -> double via the shortest roundtrip text, otherwise 1.1f would become 1.100000023841858
        case float f: return double.Parse(f.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
        case System.Numerics.BigInteger bi: return NormalizeBigInteger(bi);
        case char c: return c.ToString();
      }

      // (D) Rich CLR scalars -> their canonical JSON string representation (D5).
      //     These types cannot survive transport as such; storing them as strings makes the in-memory state
      //     identical to what any consumer would see after a roundtrip. Typed reads reconstruct them (R6).
      if (value is DateTime) {
        return ((DateTime)value).ToString("o", CultureInfo.InvariantCulture);
      }
      if (value is DateTimeOffset) {
        return ((DateTimeOffset)value).ToString("o", CultureInfo.InvariantCulture);
      }
      if (value is Guid) {
        return ((Guid)value).ToString("D");
      }
      if (value is TimeSpan) {
        return ((TimeSpan)value).ToString("c", CultureInfo.InvariantCulture);
      }
#if NET6_0_OR_GREATER
      if (value is DateOnly) {
        return ((DateOnly)value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
      }
      if (value is TimeOnly) {
        return ((TimeOnly)value).ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);
      }
#endif
      if (value is Enum) {
        //member name(s), flags as "A, B"; undefined values become their number as text - all of them are
        //accepted again by the enum step of SmartValueConverter. Names survive re-ordering of enum members.
        return value.ToString();
      }
      Uri uri = value as Uri;
      if (uri != null) {
        return uri.OriginalString;
      }
      Version version = value as Version;
      if (version != null) {
        return version.ToString();
      }
      byte[] bytes = value as byte[];
      if (bytes != null) {
        //same representation both serializers use for byte[]; must be checked before IEnumerable
        return Convert.ToBase64String(bytes);
      }

      // (E) Containers -> rebuilt canonical containers (deep copy, D6).
      IDictionary<string, object> genericMap = value as IDictionary<string, object>;
      if (genericMap != null) {
        //also covers ExpandoObject, other bags and plain Dictionary<string, object>
        Dictionary<string, object> result = new Dictionary<string, object>(genericMap.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, object> entry in genericMap) {
          result[entry.Key] = NormalizeCore(entry.Value, depth + 1);
        }
        return result;
      }
      IDictionary legacyMap = value as IDictionary;
      if (legacyMap != null) {
        //covers Dictionary<TKey, TValue> with any key/value type (it implements the non-generic IDictionary)
        Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in legacyMap) {
          result[ToKeyString(entry.Key, depth)] = NormalizeCore(entry.Value, depth + 1);
        }
        return result;
      }
      IEnumerable sequence = value as IEnumerable;
      if (sequence != null) {
        List<object> result = new List<object>();
        foreach (object item in sequence) {
          result.Add(NormalizeCore(item, depth + 1));
        }
        return result;
      }

      // (F) Custom POCO: kept by reference. Every serializer can write it; typed reads shape it on demand (D5).
      return value;
    }

    /// <summary>
    /// Newtonsoft DOM -> canonical values. JValue already carries CLR values (long, double, DateTime, Guid, ...),
    /// which are then mapped by the regular CLR rules (so Newtonsoft-guessed dates become ISO 8601 strings, L2).
    /// </summary>
    private static object NormalizeJToken(JToken token, int depth) {
      switch (token.Type) {

        case JTokenType.Object: {
            Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (JProperty property in ((JObject)token).Properties()) {
              result[property.Name] = NormalizeCore(property.Value, depth + 1);
            }
            return result;
          }

        case JTokenType.Array: {
            List<object> result = new List<object>();
            foreach (JToken item in (JArray)token) {
              if (item.Type == JTokenType.Comment) {
                continue; //comments are not data (only present with CommentHandling.Load)
              }
              result.Add(NormalizeCore(item, depth + 1));
            }
            return result;
          }

        case JTokenType.Property:
          return NormalizeCore(((JProperty)token).Value, depth);

        case JTokenType.Null:
        case JTokenType.Undefined:
        case JTokenType.Comment:
          return null;

        case JTokenType.Raw: {
            //JRaw holds unparsed JSON text -> parse WITHOUT date/float guessing, then normalize
            string rawJson = ((JValue)token).Value as string;
            if (string.IsNullOrWhiteSpace(rawJson)) {
              return null;
            }
            using (JsonTextReader reader = new JsonTextReader(new StringReader(rawJson))) {
              reader.DateParseHandling = DateParseHandling.None;
              reader.FloatParseHandling = FloatParseHandling.Decimal;
              return NormalizeCore(JToken.ReadFrom(reader), depth);
            }
          }

        case JTokenType.Constructor:
          //Newtonsoft-only extension of JSON ("new Date(...)"), has no JSON equivalent
          throw new NotSupportedException("JSON constructors are not supported as extension data.");

        default: {
            //Integer, Float, String, Boolean, Date, Bytes, Guid, Uri, TimeSpan
            JValue jValue = token as JValue;
            return NormalizeCore(jValue != null ? jValue.Value : null, depth);
          }
      }
    }

#if NETCOREAPP3_0_OR_GREATER
    /// <summary>
    /// System.Text.Json DOM -> canonical values. JsonElement keeps the raw number text, so integer vs. decimal
    /// notation (and the decimal scale) can be preserved exactly.
    /// </summary>
    private static object NormalizeJsonElement(System.Text.Json.JsonElement element, int depth) {
      EnsureDepth(depth);
      switch (element.ValueKind) {

        case System.Text.Json.JsonValueKind.Object: {
            Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (System.Text.Json.JsonProperty property in element.EnumerateObject()) {
              //duplicate member names inside one JSON object: last wins (same as the serializers)
              result[property.Name] = NormalizeJsonElement(property.Value, depth + 1);
            }
            return result;
          }

        case System.Text.Json.JsonValueKind.Array: {
            List<object> result = new List<object>();
            foreach (System.Text.Json.JsonElement item in element.EnumerateArray()) {
              result.Add(NormalizeJsonElement(item, depth + 1));
            }
            return result;
          }

        case System.Text.Json.JsonValueKind.String:
          //NO date/guid guessing here (D5)
          return element.GetString();

        case System.Text.Json.JsonValueKind.Number: {
            //integer notation -> long; anything else -> decimal (keeps the scale: "1.50" stays "1.50");
            //out of decimal range -> double; out of double range -> raw text (L3)
            long integerValue;
            if (element.TryGetInt64(out integerValue)) {
              return integerValue;
            }
            decimal decimalValue;
            if (element.TryGetDecimal(out decimalValue)) {
              return decimalValue;
            }
            double doubleValue;
            if (element.TryGetDouble(out doubleValue)) {
              return doubleValue;
            }
            return element.GetRawText();
          }

        case System.Text.Json.JsonValueKind.True:
          return true;

        case System.Text.Json.JsonValueKind.False:
          return false;

        default:
          //Null, Undefined
          return null;
      }
    }
#endif

    private static object NormalizeBigInteger(System.Numerics.BigInteger value) {
      if (value >= long.MinValue && value <= long.MaxValue) {
        return (long)value;
      }
      try {
        return (decimal)value;
      }
      catch (OverflowException) {
        return (double)value;
      }
    }

    /// <summary>
    /// Dictionary keys of non-string dictionaries become strings (JSON object member names are always strings).
    /// The key is normalized first, so e.g. a DateTime key becomes ISO 8601 and not a culture-specific text.
    /// </summary>
    private static string ToKeyString(object key, int depth) {
      return FormatScalar(NormalizeCore(key, depth + 1)) ?? string.Empty;
    }

    /// <summary>
    /// Formats a CANONICAL scalar (see <see cref="Normalize"/>) as invariant text: bool as "true"/"false"
    /// (JSON literals, not "True"), floating point numbers in their shortest roundtrip form, decimal with its scale.
    /// Also used by SmartValueConverter when a value is requested as string.
    /// </summary>
    internal static string FormatScalar(object canonicalValue) {
      if (canonicalValue == null) {
        return null;
      }
      string text = canonicalValue as string;
      if (text != null) {
        return text;
      }
      if (canonicalValue is bool) {
        return (bool)canonicalValue ? "true" : "false";
      }
      if (canonicalValue is double) {
        return ((double)canonicalValue).ToString("R", CultureInfo.InvariantCulture);
      }
      IFormattable formattable = canonicalValue as IFormattable;
      if (formattable != null) {
        return formattable.ToString(null, CultureInfo.InvariantCulture);
      }
      return canonicalValue.ToString();
    }

    private static void EnsureDepth(int depth) {
      if (depth > MaxNestingDepth) {
        throw new InvalidOperationException(
          $"The maximum nesting depth of {MaxNestingDepth} was exceeded while normalizing extension data (cyclic object graph?)."
        );
      }
    }

    #endregion

    #region " Unified Typed Read (used by the owner's GetField/TryGetField) "

    /// <summary>
    /// Unified, TYPED read across well-known properties and extension entries (R6).
    /// Resolution order:
    /// (1) well-known property of the owner (exact wire name, then case-insensitive; D2/D8),
    /// (2) extension entry with the exact key,
    /// (3) extension entry matching case-insensitively (only if unique, otherwise AmbiguousMatchException).
    /// The located value is converted by <see cref="SmartValueConverter"/>.
    /// </summary>
    /// <param name="fieldName">Wire name or CLR property name (any casing).</param>
    /// <param name="targetType">The type the consumer wants.</param>
    /// <param name="value">The converted value (only meaningful if true is returned).</param>
    /// <returns>
    /// False if the field exists neither as readable well-known property nor as extension entry.
    /// A field that exists with JSON null returns true (and throws for non-nullable value types, R8).
    /// </returns>
    internal bool TryGetField(string fieldName, Type targetType, out object value) {
      if (fieldName == null) {
        throw new ArgumentNullException(nameof(fieldName));
      }
      if (targetType == null) {
        throw new ArgumentNullException(nameof(targetType));
      }

      OwnerContract.KnownMember member;
      if (_OwnerContract.TryResolve(fieldName, out member)) {
        if (!member.IsReadable) {
          value = null;
          return false;
        }
        //the property already has its CLR type; conversion only happens if the consumer asks for another one
        object propertyValue = member.Property.GetValue(_Owner, null);
        value = SmartValueConverter.ConvertTo(propertyValue, targetType, member.Property.Name);
        return true;
      }

      string actualKey;
      if (this.TryResolveEntryKey(fieldName, out actualKey)) {
        value = SmartValueConverter.ConvertTo(_Entries[actualKey], targetType, actualKey);
        return true;
      }

      value = null;
      return false;
    }

    /// <summary>
    /// Exact key first; otherwise a UNIQUE case-insensitive match (D3). Two entries differing only by case
    /// ("foo"/"Foo") make a case-insensitive request ambiguous - we refuse to guess (R8).
    /// </summary>
    private bool TryResolveEntryKey(string fieldName, out string actualKey) {
      if (_Entries.ContainsKey(fieldName)) {
        actualKey = fieldName;
        return true;
      }
      actualKey = null;
      foreach (string candidate in _Entries.Keys) {
        if (string.Equals(candidate, fieldName, StringComparison.OrdinalIgnoreCase)) {
          if (actualKey != null) {
            throw new AmbiguousMatchException(
              $"The extension field '{fieldName}' is ambiguous: it matches '{actualKey}' and '{candidate}' case-insensitively. Request it with its exact casing."
            );
          }
          actualKey = candidate;
        }
      }
      return actualKey != null;
    }

    #endregion

    #region " IDictionary<string, object> (honest contract: extension entries only, D7) "

    /// <summary>
    /// GET: the canonical value of an EXTENSION entry (exact key, KeyNotFoundException otherwise).
    /// SET: the regular insert path (redirect to well-known properties + normalization). This is the member
    /// both serializers use to fill extension data during deserialization.
    /// </summary>
    public object this[string key] {
      get {
        return _Entries[key];
      }
      set {
        this.Store(key, value, throwOnDuplicate: false);
      }
    }

    /// <summary>Keys of the extension entries only (well-known properties are never listed, D7).</summary>
    public ICollection<string> Keys {
      get {
        return _Entries.Keys;
      }
    }

    /// <summary>Canonical values of the extension entries only.</summary>
    public ICollection<object> Values {
      get {
        return _Entries.Values;
      }
    }

    /// <summary>Number of extension entries (well-known properties are not counted, D7).</summary>
    public int Count {
      get {
        return _Entries.Count;
      }
    }

    public bool IsReadOnly {
      get {
        return false;
      }
    }

    /// <summary>
    /// Insert with Add semantics. Note: a key matching a well-known property is redirected (D2) and
    /// therefore never causes a duplicate-key exception.
    /// </summary>
    public void Add(string key, object value) {
      this.Store(key, value, throwOnDuplicate: true);
    }

    /// <inheritdoc cref="Add(string, object)"/>
    public void Add(KeyValuePair<string, object> item) {
      this.Store(item.Key, item.Value, throwOnDuplicate: true);
    }

    public void Clear() {
      _Entries.Clear();
    }

    public bool Contains(KeyValuePair<string, object> item) {
      return ((ICollection<KeyValuePair<string, object>>)_Entries).Contains(item);
    }

    /// <summary>Exact (ordinal) key check on extension entries only.</summary>
    public bool ContainsKey(string key) {
      return _Entries.ContainsKey(key);
    }

    public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex) {
      ((ICollection<KeyValuePair<string, object>>)_Entries).CopyTo(array, arrayIndex);
    }

    /// <summary>
    /// Enumerates the extension entries only. This is what the serializers flatten onto the owner's root level;
    /// since well-known properties are never contained, no key can be written twice (D7).
    /// </summary>
    public IEnumerator<KeyValuePair<string, object>> GetEnumerator() {
      return _Entries.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() {
      return _Entries.GetEnumerator();
    }

    public bool Remove(string key) {
      return _Entries.Remove(key);
    }

    public bool Remove(KeyValuePair<string, object> item) {
      return ((ICollection<KeyValuePair<string, object>>)_Entries).Remove(item);
    }

    /// <summary>Exact (ordinal) lookup on extension entries only. Use the owner's GetField for typed/case-tolerant reads.</summary>
    public bool TryGetValue(string key, out object value) {
      return _Entries.TryGetValue(key, out value);
    }

    #endregion

    #region " Owner Contract (cached reflection metadata per owner type) "

    /// <summary>
    /// The well-known members of an owner type, indexed by their wire names.
    /// Built once per runtime type and cached for the lifetime of the AppDomain.
    /// Attributes are detected BY NAME (CustomAttributeData), so neither System.Text.Json nor a specific
    /// Newtonsoft version has to be referenced for the detection itself (R7).
    /// </summary>
    private sealed class OwnerContract {

      private const string _NewtonsoftExtensionData = "Newtonsoft.Json.JsonExtensionDataAttribute";
      private const string _NewtonsoftIgnore = "Newtonsoft.Json.JsonIgnoreAttribute";
      private const string _NewtonsoftProperty = "Newtonsoft.Json.JsonPropertyAttribute";
      private const string _StjExtensionData = "System.Text.Json.Serialization.JsonExtensionDataAttribute";
      private const string _StjIgnore = "System.Text.Json.Serialization.JsonIgnoreAttribute";
      private const string _StjPropertyName = "System.Text.Json.Serialization.JsonPropertyNameAttribute";
      private const string _StjInclude = "System.Text.Json.Serialization.JsonIncludeAttribute";

      /// <summary>JsonIgnoreCondition.Always (STJ). Only this condition removes a property from deserialization.</summary>
      private const int _StjIgnoreConditionAlways = 1;

      private static readonly ConcurrentDictionary<Type, OwnerContract> _Cache = new ConcurrentDictionary<Type, OwnerContract>();

      /// <summary>Exact (ordinal) wire name -> member.</summary>
      private readonly Dictionary<string, KnownMember> _ByWireName = new Dictionary<string, KnownMember>(StringComparer.Ordinal);

      /// <summary>Case-insensitive wire name -> all members matching it (more than one = ambiguous).</summary>
      private readonly Dictionary<string, KnownMember[]> _ByFoldedWireName;

      public static OwnerContract For(Type ownerType) {
        return _Cache.GetOrAdd(ownerType, (t) => new OwnerContract(t));
      }

      private OwnerContract(Type ownerType) {

        // Public instance properties without index parameters. Properties hidden via 'new' appear once per
        // declaring type -> keep the most derived declaration only.
        IEnumerable<PropertyInfo> properties = ownerType
          .GetProperties(BindingFlags.Instance | BindingFlags.Public)
          .Where((p) => p.GetIndexParameters().Length == 0)
          .GroupBy((p) => p.Name, StringComparer.Ordinal)
          .Select((g) => g.OrderByDescending((p) => GetInheritanceDepth(p.DeclaringType)).First());

        Dictionary<string, List<KnownMember>> folded = new Dictionary<string, List<KnownMember>>(StringComparer.OrdinalIgnoreCase);

        foreach (PropertyInfo property in properties) {
          IList<CustomAttributeData> attributes = property.GetCustomAttributesData();

          // the bag property itself is not a well-known member
          if (HasAttribute(attributes, _NewtonsoftExtensionData) || HasAttribute(attributes, _StjExtensionData)) {
            continue;
          }
          // ignored properties are not part of the wire contract -> their keys are plain extension data (D8)
          if (IsIgnoredBySerializer(attributes)) {
            continue;
          }

          KnownMember member = new KnownMember(property, attributes);
          foreach (string wireName in member.WireNames) {
            if (!_ByWireName.ContainsKey(wireName)) {
              _ByWireName[wireName] = member;
            }
            List<KnownMember> bucket;
            if (!folded.TryGetValue(wireName, out bucket)) {
              bucket = new List<KnownMember>();
              folded[wireName] = bucket;
            }
            if (!bucket.Contains(member)) {
              bucket.Add(member);
            }
          }
        }

        _ByFoldedWireName = folded.ToDictionary((kvp) => kvp.Key, (kvp) => kvp.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
      }

      /// <summary>
      /// Exact wire name first; otherwise a unique case-insensitive match. Two well-known members that differ
      /// only by case (e.g. "Foo" and "FOO") make a case-variant key ambiguous -> exception instead of guessing (R8).
      /// </summary>
      public bool TryResolve(string key, out KnownMember member) {
        if (_ByWireName.TryGetValue(key, out member)) {
          return true;
        }
        KnownMember[] candidates;
        if (_ByFoldedWireName.TryGetValue(key, out candidates)) {
          if (candidates.Length == 1) {
            member = candidates[0];
            return true;
          }
          throw new AmbiguousMatchException(
            $"The member '{key}' matches several well-known properties case-insensitively ({string.Join(", ", candidates.Select((c) => c.Property.Name))}). Use the exact casing."
          );
        }
        member = null;
        return false;
      }

      private static bool IsIgnoredBySerializer(IList<CustomAttributeData> attributes) {
        if (HasAttribute(attributes, _NewtonsoftIgnore)) {
          return true;
        }
        CustomAttributeData stjIgnore = FindAttribute(attributes, _StjIgnore);
        if (stjIgnore == null) {
          return false;
        }
        // [JsonIgnore] without Condition means Always; WhenWritingNull/WhenWritingDefault/Never still deserialize
        foreach (CustomAttributeNamedArgument namedArgument in stjIgnore.NamedArguments) {
          if (namedArgument.MemberName == "Condition" && namedArgument.TypedValue.Value != null) {
            return Convert.ToInt32(namedArgument.TypedValue.Value, CultureInfo.InvariantCulture) == _StjIgnoreConditionAlways;
          }
        }
        return true;
      }

      private static int GetInheritanceDepth(Type type) {
        int depth = 0;
        while (type != null) {
          depth++;
          type = type.BaseType;
        }
        return depth;
      }

      internal static bool HasAttribute(IList<CustomAttributeData> attributes, string attributeFullName) {
        return FindAttribute(attributes, attributeFullName) != null;
      }

      internal static CustomAttributeData FindAttribute(IList<CustomAttributeData> attributes, string attributeFullName) {
        foreach (CustomAttributeData attribute in attributes) {
          if (attribute.AttributeType.FullName == attributeFullName) {
            return attribute;
          }
        }
        return null;
      }

      /// <summary>Reads a string from the first constructor argument or from a named argument.</summary>
      internal static string GetStringArgument(IList<CustomAttributeData> attributes, string attributeFullName, string namedArgumentName) {
        CustomAttributeData attribute = FindAttribute(attributes, attributeFullName);
        if (attribute == null) {
          return null;
        }
        foreach (CustomAttributeTypedArgument argument in attribute.ConstructorArguments) {
          string text = argument.Value as string;
          if (argument.ArgumentType == typeof(string) && !string.IsNullOrEmpty(text)) {
            return text;
          }
        }
        if (namedArgumentName != null) {
          foreach (CustomAttributeNamedArgument namedArgument in attribute.NamedArguments) {
            string text = namedArgument.TypedValue.Value as string;
            if (namedArgument.MemberName == namedArgumentName && !string.IsNullOrEmpty(text)) {
              return text;
            }
          }
        }
        return null;
      }

      /// <summary>
      /// One well-known property of the owner type, with everything the bag needs to know about it.
      /// </summary>
      internal sealed class KnownMember {

        public readonly PropertyInfo Property;

        /// <summary>
        /// The names under which the property travels on the wire: the effective name for Newtonsoft
        /// ([JsonProperty] or CLR name) and for STJ ([JsonPropertyName] or CLR name). Usually just the CLR name.
        /// </summary>
        public readonly string[] WireNames;

        /// <summary>Public getter, or a non-public one opted in via [JsonInclude]/[JsonProperty].</summary>
        public readonly bool IsReadable;

        /// <summary>
        /// Public setter (including init), or a non-public one opted in via [JsonInclude]/[JsonProperty].
        /// Not writable = output-only, incoming values are discarded like the serializers do (D8).
        /// </summary>
        public readonly bool IsWritable;

        public KnownMember(PropertyInfo property, IList<CustomAttributeData> attributes) {
          this.Property = property;

          string newtonsoftWireName = GetStringArgument(attributes, _NewtonsoftProperty, "PropertyName") ?? property.Name;
          string stjWireName = GetStringArgument(attributes, _StjPropertyName, null) ?? property.Name;
          this.WireNames = new string[] { newtonsoftWireName, stjWireName }.Distinct(StringComparer.Ordinal).ToArray();

          bool explicitlyIncluded = HasAttribute(attributes, _StjInclude) || HasAttribute(attributes, _NewtonsoftProperty);
          MethodInfo getter = property.GetGetMethod(true);
          MethodInfo setter = property.GetSetMethod(true);
          this.IsReadable = getter != null && (getter.IsPublic || explicitlyIncluded);
          this.IsWritable = setter != null && (setter.IsPublic || explicitlyIncluded);
        }

      }

    }

    #endregion

    #region " Debugger "

    /// <summary>Shows the extension entries flat in the debugger (no "_Entries" / "Keys" / "Values" clutter).</summary>
    internal sealed class DebugView {

      private readonly SmartExtensionBag _Bag;

      public DebugView(SmartExtensionBag bag) {
        _Bag = bag;
      }

      [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
      public KeyValuePair<string, object>[] Entries {
        get {
          return _Bag._Entries.ToArray();
        }
      }

    }

    #endregion

  }

}
