using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Collaboration.WorkTracking {

  public partial class WorkItem {

    #region " Extension Data "

    /// <summary>
    /// Backing field. Created lazily because field initializers cannot pass <c>this</c> to the bag.
    /// </summary>
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private SmartExtensionBag _AdditionalFields = null;

    /// <summary>
    /// All members of the JSON object that are NOT covered by a well-known property of this contract.
    /// Serializers (Newtonsoft.Json and System.Text.Json, default settings) fill this dictionary during
    /// deserialization and flatten it onto the root level during serialization - in OpenAPI/UJMW terms these
    /// are the "additionalProperties" of the contract.
    /// </summary>
    /// <remarks>
    ///   * Keys keep their exact casing; keys differing only by case are separate entries.
    ///   * Values are stored in their canonical JSON form only: string, bool, long, decimal, double,
    ///     Dictionary&lt;string, object&gt;, List&lt;object&gt; (or a POCO set by code). No serializer-specific types,
    ///     so the item can be deserialized by one serializer and serialized by the other.
    ///   * Writing a key that matches a well-known property (also in another casing, e.g. "title")
    ///     writes that property instead; it never appears here.
    ///   * For typed access (Guid, DateTime, enums, Int32, POCOs, ...) and for access across
    ///     well-known and extension fields use <see cref="GetField{TValue}"/> / <see cref="SetField{TValue}"/>.
    /// </remarks>
    [Newtonsoft.Json.JsonExtensionData]
    [System.Text.Json.Serialization.JsonExtensionData]
    public IDictionary<string, object> AdditionalFields {
      get {
        return this.ExtensionBag;
      }
      set {
        // A serializer may assign its own dictionary; regular code may assign another item's extensions.
        // Adopt binds everything to THIS owner (redirect + normalization, deep copy). null = empty.
        _AdditionalFields = SmartExtensionBag.Adopt(this, value);
      }
    }

    /// <summary>
    /// Typed access to the bag. Private and non-serializable on purpose: neither serializers nor OpenAPI see it.
    /// </summary>
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private SmartExtensionBag ExtensionBag {
      get {
        if (_AdditionalFields == null) {
          _AdditionalFields = new SmartExtensionBag(this);
        }
        return _AdditionalFields;
      }
    }

    #endregion

    #region " Unified Field Access "

    /// <summary>
    /// Untyped access across well-known properties and extension fields.
    /// GET returns the property value or the canonical extension value (null if the field does not exist).
    /// SET writes the property (also for case variants of its name) or the extension field.
    /// </summary>
    /// <param name="fieldName">CLR property name, wire name or extension key (any casing).</param>
    public object this[string fieldName] {
      get {
        return this.GetField<object>(fieldName, null);
      }
      set {
        this.SetField(fieldName, value);
      }
    }

    /// <summary>
    /// Reads a field and converts it into the type the CALLER needs. Only the caller knows that type:
    /// JSON itself cannot carry it (a Guid travels as string, an Int32 as number).
    /// </summary>
    /// <typeparam name="TValue">
    /// The requested type, e.g. int, long?, Guid, DateTime, an enum (from name or number), string[], List&lt;T&gt;,
    /// Dictionary&lt;string, T&gt; or a POCO. Use <c>object</c> to get the canonical (live) value.
    /// </typeparam>
    /// <param name="fieldName">
    /// CLR property name, wire name or extension key. Resolution: well-known property, exact extension key,
    /// unique case-insensitive extension key.
    /// </param>
    /// <param name="defaultValue">Returned ONLY if the field does not exist at all.</param>
    /// <exception cref="InvalidCastException">
    /// The field exists but cannot be represented as <typeparamref name="TValue"/> (e.g. 1.5 requested as int,
    /// JSON null requested as non-nullable value type). Nothing is silently rounded or replaced.
    /// </exception>
    /// <exception cref="System.Reflection.AmbiguousMatchException">
    /// The name matches several fields case-insensitively; request it with its exact casing.
    /// </exception>
    public TValue GetField<TValue>(string fieldName, TValue defaultValue = default(TValue)) {
      object value;
      if (this.ExtensionBag.TryGetField(fieldName, typeof(TValue), out value)) {
        return (TValue)value;
      }
      return defaultValue;
    }

    /// <summary>
    /// Like <see cref="GetField{TValue}"/>, but reports whether the field exists instead of using a default.
    /// Conversion errors still throw - a field that exists but has the wrong shape is a contract violation,
    /// not an absent field.
    /// </summary>
    public bool TryGetField<TValue>(string fieldName, out TValue value) {
      object rawValue;
      if (this.ExtensionBag.TryGetField(fieldName, typeof(TValue), out rawValue)) {
        value = (TValue)rawValue;
        return true;
      }
      value = default(TValue);
      return false;
    }

    /// <summary>
    /// Writes a field. A name matching a well-known property (any casing) writes the property (converted to its
    /// type); every other name becomes an extension field in its canonical JSON form (a Guid is stored as its
    /// "D" string, a DateTime as ISO 8601, an enum by name, containers as deep copies).
    /// </summary>
    public void SetField<TValue>(string fieldName, TValue value) {
      this.ExtensionBag[fieldName] = value;
    }

    /// <summary>
    /// Creates a concrete work item from a raw dictionary (e.g. inside an adapter). Keys matching well-known
    /// properties fill those properties (any casing), all other keys become extension fields.
    /// </summary>
    protected static TConcreteClass FromDictionary<TConcreteClass>(IDictionary<string, object> input)
      where TConcreteClass : WorkItem, new() {
      TConcreteClass newInstance = new TConcreteClass();
      newInstance.AdditionalFields = input;
      return newInstance;
    }

    #endregion

    #region " Compatibility (previous API) "

    /// <summary>Previous API. Use <see cref="GetField{TValue}"/>.</summary>
    [Obsolete("Use GetField<TValue>(fieldName, defaultValue).")]
    public TValue GetCustomField<TValue>(string fieldName, TValue defaultValue) {
      return this.GetField(fieldName, defaultValue);
    }

    /// <summary>Previous API. Use <see cref="SetField{TValue}"/>.</summary>
    /// <param name="fieldName">Field name.</param>
    /// <param name="value">Value to write.</param>
    /// <param name="customSerializer">
    /// Optional pre-conversion (kept for compatibility). The bag normalizes values anyway,
    /// so it is no longer required.
    /// </param>
    [Obsolete("Use SetField<TValue>(fieldName, value).")]
    public void SetCustomField<TValue>(string fieldName, TValue value, Func<TValue, object> customSerializer = null) {
      if (customSerializer != null) {
        this.ExtensionBag[fieldName] = customSerializer(value);
        return;
      }
      this.SetField(fieldName, value);
    }

    #endregion

  }

}
