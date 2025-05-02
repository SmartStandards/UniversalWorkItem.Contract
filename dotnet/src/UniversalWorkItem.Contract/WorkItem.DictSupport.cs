using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Xml;

namespace Collaboration.WorkTracking {

  public partial class WorkItem : IDictionary<string, object> {

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private const string _TypePropertySuffix = "`T";

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private IDictionary<string, object> _InnerDictionary = new Dictionary<string, object>();

    protected static TConcreteClass FromDictionary<TConcreteClass>(IDictionary<string, object> input)
      where TConcreteClass : WorkItem, new() {
      TConcreteClass newInstance = new TConcreteClass();
      newInstance._InnerDictionary = input;
      return newInstance;
    }

    /// <summary>
    /// Access to Custom-Fields
    /// </summary>
    /// <param name="fieldName"></param>
    /// <returns></returns>
    public object this[string fieldName] {
      get {
        return this.GetValue<object>(fieldName, null);
      }
      set {
        this.SetValue(fieldName, value);
      }
    }

    /// <summary></summary>
    /// <typeparam name="TValue"></typeparam>
    /// <param name="fieldName"></param>
    /// <param name="value"></param>
    /// <param name="customSerializer">
    /// Should usually return a STRING!
    /// But in special cases other (json compatible) target types can be used:
    /// (LONG)-INT | STRING ARRAY | INT ARRAY
    /// </param>
    public void SetValue<TValue>(string fieldName, TValue value, Func<TValue, object> customSerializer = null) {

      PropertyInfo explicitProp = this.ExplicitProperties.Where((p) => p.Name.Equals(fieldName, StringComparison.CurrentCultureIgnoreCase)).FirstOrDefault();
      if (explicitProp != null) {
        explicitProp.SetValue(this, value);
        return;
      }

      _InnerDictionary[fieldName] = value; //TODO: mals sehen ob das immer reicht..,

    }

    internal static string GetTypeHintByType(Type t, out Func<object, object> serializeMethod, bool skipAssumable = true) {
      serializeMethod = (o) => o;
      if (t == typeof(byte[])) {
        return "binary";
      }
      string arraySuffix = string.Empty;
      if (t.IsArray) {
        t = t.GetElementType();
        if (t == null) {
          t = typeof(object);
        }
        arraySuffix = "[]";
      }
      if (t == typeof(string)) {
        if (skipAssumable) return null;
        return "string" + arraySuffix;
      }
      if (t == typeof(DateTime)) {
        return "date" + arraySuffix;
      }
      if (t == typeof(Guid)) {
        return "uuid" + arraySuffix;
      }
      if (t == typeof(bool)) {
        if (skipAssumable) return null;
        return "bool" + arraySuffix;
      }
      if (t == typeof(byte)) {
        return "int8" + arraySuffix;
      }
      if (t == typeof(Int16)) { //int
        return "int16" + arraySuffix;
      }
      if (t == typeof(Int32)) { //int
        return "int32" + arraySuffix;
      }
      if (t == typeof(Int64)) {  //long int
        if (skipAssumable) return null;
        return "int64" + arraySuffix;
      }
      if (t == typeof(float)) {
        return "float32" + arraySuffix;
      }
      if (t == typeof(Double)) { //number
        if (skipAssumable) return null;
        return "float64" + arraySuffix;
      }
      if (t == typeof(Decimal)) {
        return "decimal" + arraySuffix;
      }
      serializeMethod = (o) => JsonConvert.SerializeObject(o);
      return "object" + arraySuffix + ":" + t.FullName;
    }

    //private static Type GetTargetTypeByTypeHint(
    //  string typeHint, Type transportType, out Func<object, object> deserializeMethod, Type expectedType = null
    //) {
    //  deserializeMethod = (o) => o;


    //  if (typeHint == null) {
    //    if (expectedType != null) {
    //      typeHint = GetTypeHintByType(expectedType, out var dummy,false);
    //    }
    //    else if (transportType != null) {
    //      typeHint = GetTypeHintByType(transportType, out var dummy, false); ;
    //    }
    //    else {
    //      deserializeMethod = (o) => o;
    //      return typeof(object);
    //    }
    //  }

    //  int iSplt = typeHint.IndexOf(":");
    //  string objTypeName = null;
    //  bool isArray = false;
    //  Type concreteType;

    //  if (iSplt >= 0) {
    //    objTypeName = typeHint.Substring(iSplt + 1);
    //    typeHint = typeHint.Substring(0, iSplt);
    //  }
    //  if (typeHint.EndsWith("[]")) {
    //    isArray = true;
    //    typeHint = typeHint.Substring(0, typeHint.Length - 2);
    //  }

    //  if (typeHint == "string") {
    //    deserializeMethod = (o) => Convert.ToString(o);
    //    concreteType = typeof(string);
    //  }
    //  else if (typeHint == "date") {
    //    deserializeMethod = (o) => {
    //      if (o is DateTime) return ((DateTime)o);
    //      return DateTime.Parse(o.ToString());
    //    };
    //    concreteType = typeof(string);
    //  }
    //  else if (typeHint == "uuid") {
    //    deserializeMethod = (o) => Guid.Parse(o.ToString());
    //    concreteType = typeof(string);
    //  }
    //  else if (typeHint == "bool") {
    //    deserializeMethod = (o) => Convert.ToBoolean(o);
    //    concreteType = typeof(string);
    //  }
    //  else if (typeHint == "int8") {
    //    deserializeMethod = (o) => Convert.ToByte(o);
    //    concreteType = typeof(string);
    //  }
    //  else if (typeHint == "int16") {
    //    deserializeMethod = (o) => Convert.ToInt16(o);
    //    concreteType = typeof(string);
    //  }
    //  else if (typeHint == "int32") {
    //    deserializeMethod = (o) => Convert.ToInt32(o);
    //    concreteType = typeof(string);
    //  }
    //  else if (typeHint == "int64") {
    //    deserializeMethod = (o) => Convert.ToInt64(o);
    //    concreteType = typeof(string);
    //  }
    //  else if (typeHint == "float32") {
    //    deserializeMethod = (o) => Convert.ToSingle(o);
    //    concreteType = typeof(string);
    //  }
    //  else if (typeHint == "float64") {
    //    deserializeMethod = (o) => Convert.ToDouble(o);
    //    concreteType = typeof(string);
    //  }
    //  else if (typeHint == "decimal") {
    //    deserializeMethod = (o) => Convert.ToDecimal(o);
    //    concreteType = typeof(string);
    //  }
    //  else if (objTypeName != null) {
    //    concreteType = Type.GetType(objTypeName);
    //    deserializeMethod = (o) => JsonConvert.DeserializeObject(o.ToString(), concreteType);
    //  }
    //  else {
    //    concreteType = expectedType; //oder wenn der null?
    //    deserializeMethod = (o) => o;
    //  }

    //  if (isArray) {
    //    var singleItemDeserializeMethod = deserializeMethod;
    //    deserializeMethod = (o) => ((IEnumerable)o).OfType<object>().Select(singleItemDeserializeMethod);
    //  }

    //  return concreteType;
    //}


    public TValue GetValue<TValue>(string fieldName, TValue defaultValue) {

      PropertyInfo explicitProp = this.ExplicitProperties.Where((p) => p.Name.Equals(fieldName, StringComparison.CurrentCultureIgnoreCase)).FirstOrDefault();
      if (explicitProp != null) {
        object valueFromProp = explicitProp.GetValue(this);
        return (TValue)(valueFromProp);
      }

      if (!_InnerDictionary.ContainsKey(fieldName)) {
        return defaultValue;
      }

      _InnerDictionary.TryGetValue(fieldName + _TypePropertySuffix, out var typeHint);
      object rawValue = _InnerDictionary[fieldName];
      object convertedValue = ToExpectedType(rawValue, typeof(TValue), (string)typeHint);

      return (TValue)(convertedValue);
    }

    private object ToExpectedType(object rawValue, Type expectedType, string typeHint = null) {

      if (rawValue == null) {
        //TODO: bei value-typed  hier eher den Default initialisieren
        return null;
      }
      Type incommingRawType = rawValue.GetType();

      if (typeHint != null && typeHint.StartsWith("object:")) {
        Type resolvedObjectType = null;
        try {
          resolvedObjectType = Type.GetType(typeHint.Substring(7));
        }
        catch {
        }
        if (resolvedObjectType != null && expectedType.IsAssignableFrom(resolvedObjectType)) {
          expectedType = resolvedObjectType; //use the more-concrete one
        }
      }

#if NET5_0_OR_GREATER
      if (incommingRawType == typeof(System.Text.Json.JsonElement)) {
        string reSerializedValue = ((System.Text.Json.JsonElement)rawValue).GetRawText();
        return JsonConvert.DeserializeObject(reSerializedValue, expectedType);
      }
#endif

      if (expectedType == typeof(string)) {
        return rawValue.ToString();
      }
      if (expectedType == typeof(DateTime)) {
        return DateTime.Parse(rawValue.ToString());
      }
      if (expectedType == typeof(Guid)) {
        return Guid.Parse(rawValue.ToString());
      }
      if (expectedType.IsPrimitive) {
        return Convert.ChangeType(rawValue, expectedType);
      }

      return JsonConvert.DeserializeObject(JsonConvert.SerializeObject(rawValue), expectedType);
    }


    #region " IDictionary (private) "

    //wird vom DESERILAIZER genutzt
    object IDictionary<string, object>.this[string key] {
      get {
        return _InnerDictionary[key];
      }
      set {
        PropertyInfo explicitProp = this.ExplicitProperties.Where((p) => p.Name.Equals(key, StringComparison.CurrentCultureIgnoreCase)).FirstOrDefault();

        if (explicitProp != null) {
          try {

            /*
             *  nicht primitive werte (objecckte) werden nochmal neu serialisertund als json string hinterlegrt
             *  -> müssen ja eh in einem 2. schritt nue deserialiseer werden
             * 
             *  vorhandene props werden auf den zieltyp konvertiert
             *  
             *  nichtvrohandene gehen roh ins dict und werden beim lesezugriff konvertiert
             *  beim schreibzugriff muss es einen typ geben, allein für den fall dass es ull ist
             *  -> inferring ist erlauabt
             * 
             */

            //TODO: wie köne nwir hier an das typehint kommen und es verarbeittn
            object converted = ToExpectedType(value, explicitProp.PropertyType, typeHint: null);
            explicitProp.SetValue(this, converted);


            if (value != null && !explicitProp.PropertyType.IsAssignableFrom(value.GetType())) {
              if (explicitProp.PropertyType.IsPrimitive) {
                explicitProp.SetValue(this, Convert.ChangeType(value, explicitProp.PropertyType));
              }
#if NET5_0_OR_GREATER
              else if (value.GetType() == typeof(System.Text.Json.JsonElement)) {
                string serialized = ((System.Text.Json.JsonElement)value).GetRawText();
                //nochmal neu mit typen-metadaten deserialisieren
                explicitProp.SetValue(this, JsonConvert.DeserializeObject(serialized, explicitProp.PropertyType));
              }
#endif
              else {


                string serialized = JsonConvert.SerializeObject(value, Newtonsoft.Json.Formatting.Indented);
                //nochmal neu mit typen-metadaten deserialisieren
                explicitProp.SetValue(this, JsonConvert.DeserializeObject(serialized, explicitProp.PropertyType));
              }
            }
            else {
              explicitProp.SetValue(this, value);
            }

            //HIWR PUSH KONVERTIERUNG in proeprty

          }
          catch {
          }
        }
        else {
          _InnerDictionary[key] = value;
        }
      }
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private PropertyInfo[] _ExplicitProperties = null;
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private PropertyInfo[] ExplicitProperties {
      get {
        if (_ExplicitProperties == null) {
          _ExplicitProperties = this.GetType().GetProperties(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy
          ).Where(
            (p) => p.GetIndexParameters().Length == 0 && !p.DeclaringType.Name.StartsWith("IDictionary")
          ).ToArray();
        }
        return _ExplicitProperties;
      }
    }
    IEnumerator<KeyValuePair<string, object>> IEnumerable<KeyValuePair<string, object>>.GetEnumerator() {

      foreach (var explicitProperty in this.ExplicitProperties) {
        object value = null;
        try {
          value = explicitProperty.GetValue(this);
        }
        catch (Exception ex) {
          value = null;
        }

        yield return new KeyValuePair<string, object>(explicitProperty.Name, value);

        if (value != null && value.GetType() != explicitProperty.PropertyType) {
          string typeHint = GetTypeHintByType(value.GetType(), out var dummy, false);
          yield return new KeyValuePair<string, object>(explicitProperty.Name + _TypePropertySuffix, typeHint);
        }

      }

      foreach (var kvp in _InnerDictionary) {

        yield return kvp;

        if (kvp.Value != null) {
          string typeHint = GetTypeHintByType(kvp.Value.GetType(), out var dummy);
          if (typeHint != null) {
            yield return new KeyValuePair<string, object>(kvp.Key + _TypePropertySuffix, typeHint);
          }
        }
      }

    }

    IEnumerator IEnumerable.GetEnumerator() {
      //TODO: muss auch die obere ebene können
      return _InnerDictionary.GetEnumerator();
    }

    bool IDictionary<string, object>.ContainsKey(string key) => _InnerDictionary.ContainsKey(key);
    void IDictionary<string, object>.Add(string key, object value) => _InnerDictionary.Add(key, value);
    bool IDictionary<string, object>.Remove(string key) => _InnerDictionary.Remove(key);
    bool IDictionary<string, object>.TryGetValue(string key, out object value) => _InnerDictionary.TryGetValue(key, out value);

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    ICollection<string> IDictionary<string, object>.Keys => _InnerDictionary.Keys;

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    ICollection<object> IDictionary<string, object>.Values => _InnerDictionary.Values;

    void ICollection<KeyValuePair<string, object>>.Add(KeyValuePair<string, object> item) => _InnerDictionary.Add(item);

    void ICollection<KeyValuePair<string, object>>.Clear() => _InnerDictionary.Clear();

    bool ICollection<KeyValuePair<string, object>>.Contains(KeyValuePair<string, object> item) => _InnerDictionary.Contains(item);

    void ICollection<KeyValuePair<string, object>>.CopyTo(KeyValuePair<string, object>[] array, int arrayIndex) => _InnerDictionary.CopyTo(array, arrayIndex);

    bool ICollection<KeyValuePair<string, object>>.Remove(KeyValuePair<string, object> item) => _InnerDictionary.Remove(item);

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    int ICollection<KeyValuePair<string, object>>.Count => _InnerDictionary.Count;

    //int ICollection<KeyValuePair<string, object>>.Count { get { return _InnerDictionary.Count;} }

    //int ICollection<KeyValuePair<string, object>>.Count { 
    //  get { return _InnerDictionary.Count; }
    //}

    //int ICollection<KeyValuePair<string, object>>.Count {
    //  get => _InnerDictionary.Count; 
    //

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    bool ICollection<KeyValuePair<string, object>>.IsReadOnly => _InnerDictionary.IsReadOnly;

    #endregion

    #region " Intellisense-Tweaks "

    [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
    private KeyValuePair<string, object>[] CustomFields {
      get {
        return _InnerDictionary.Where((kvp) => !kvp.Key.EndsWith(_TypePropertySuffix)).ToArray();
      }
    }

    #endregion

  }

}
