using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Newtonsoft.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Text.Json;
using StjJsonSerializer = System.Text.Json.JsonSerializer;

#nullable disable

namespace Collaboration.WorkTracking {

  [TestClass]
  public class Tests {

    /// <summary>
    /// Verifies canonical insert behavior without involving WorkItem.
    /// </summary>
    [TestMethod()]
    public void AdditionalFields_NormalizeRichScalarsOnInsert() {
      TestAdditionalFieldsContract item = new TestAdditionalFieldsContract();
      DateTime birthday = new DateTime(1984, 10, 18, 0, 0, 0, DateTimeKind.Unspecified);
      Guid id = Guid.Parse("3C668CCB-AC1A-D9AE-49A0-7E19623A58D2");

      item.SetField("AuthorShoeSize", 44);
      item.SetField("AuthorBirthday", birthday);
      item.SetField("AnotherUuid", id);

      Assert.AreEqual(44L, item.AdditionalFields["AuthorShoeSize"]);
      Assert.AreEqual("1984-10-18T00:00:00.0000000", item.AdditionalFields["AuthorBirthday"]);
      Assert.AreEqual("3c668ccb-ac1a-d9ae-49a0-7e19623a58d2", item.AdditionalFields["AnotherUuid"]);

      Assert.AreEqual(44, item.GetField<int>("AuthorShoeSize"));
      Assert.AreEqual(birthday, item.GetField<DateTime>("AuthorBirthday"));
      Assert.AreEqual(id, item.GetField<Guid>("AnotherUuid"));
    }

    /// <summary>
    /// Verifies Newtonsoft.Json flat roundtrip behavior.
    /// </summary>
    [TestMethod()]
    public void AdditionalFields_NewtonsoftRoundtrip_FlattensAndKeepsCanonicalValues() {
      TestAdditionalFieldsContract source = CreateFilledContract();

      string json = JsonConvert.SerializeObject(source, Formatting.Indented);

      Assert.IsFalse(json.Contains("AdditionalFields"));
      Assert.IsTrue(json.Contains("AuthorShoeSize"));
      Assert.IsTrue(json.Contains("Nested"));

      TestAdditionalFieldsContract deserialized = JsonConvert.DeserializeObject<TestAdditionalFieldsContract>(json);

      Assert.AreEqual("Known title", deserialized.Title);
      Assert.AreEqual(44L, deserialized.AdditionalFields["AuthorShoeSize"]);
      Assert.AreEqual(44, deserialized.GetField<int>("AuthorShoeSize"));
      Assert.AreEqual(Guid.Parse("3C668CCB-AC1A-D9AE-49A0-7E19623A58D2"), deserialized.GetField<Guid>("AnotherUuid"));
      AssertDoesNotContainSerializerDom(deserialized.AdditionalFields);
    }

    /// <summary>
    /// Verifies System.Text.Json flat roundtrip behavior.
    /// </summary>
    [TestMethod()]
    public void AdditionalFields_SystemTextJsonRoundtrip_FlattensAndRemovesJsonElements() {
      TestAdditionalFieldsContract source = CreateFilledContract();

      string json = StjJsonSerializer.Serialize(source);

      Assert.IsFalse(json.Contains("AdditionalFields"));
      Assert.IsTrue(json.Contains("AuthorShoeSize"));

      TestAdditionalFieldsContract deserialized = StjJsonSerializer.Deserialize<TestAdditionalFieldsContract>(json);

      Assert.AreEqual("Known title", deserialized.Title);
      Assert.AreEqual(44L, deserialized.AdditionalFields["AuthorShoeSize"]);
      Assert.AreEqual(new DateTime(1984, 10, 18), deserialized.GetField<DateTime>("AuthorBirthday"));
      AssertDoesNotContainSerializerDom(deserialized.AdditionalFields);
    }

    /// <summary>
    /// Verifies mixed serializer pipelines.
    /// </summary>
    [TestMethod()]
    public void AdditionalFields_MixedSerializerRoundtrip_KeepsData() {
      TestAdditionalFieldsContract source = CreateFilledContract();

      string newtonsoftJson = JsonConvert.SerializeObject(source);
      TestAdditionalFieldsContract stjRead = StjJsonSerializer.Deserialize<TestAdditionalFieldsContract>(newtonsoftJson);
      string stjJson = StjJsonSerializer.Serialize(stjRead);
      TestAdditionalFieldsContract newtonsoftRead = JsonConvert.DeserializeObject<TestAdditionalFieldsContract>(stjJson);

      Assert.AreEqual("Known title", newtonsoftRead.Title);
      Assert.AreEqual(44, newtonsoftRead.GetField<int>("AuthorShoeSize"));
      Assert.AreEqual("value", newtonsoftRead.GetField<Dictionary<string, object>>("Nested")["Child"]);
      AssertDoesNotContainSerializerDom(newtonsoftRead.AdditionalFields);
    }

    /// <summary>
    /// Verifies that case variants of known members are redirected to typed properties.
    /// </summary>
    [TestMethod()]
    public void AdditionalFields_SystemTextJsonCaseVariant_RedirectsToKnownProperty() {
      string json = "{\"title\":\"Redirected title\",\"Extension\":42}";

      TestAdditionalFieldsContract item = StjJsonSerializer.Deserialize<TestAdditionalFieldsContract>(json);

      Assert.AreEqual("Redirected title", item.Title);
      Assert.IsFalse(item.AdditionalFields.ContainsKey("title"));
      Assert.AreEqual(42, item.GetField<int>("Extension"));
    }

    /// <summary>
    /// Verifies ordinal extension keys and ambiguous fallback detection.
    /// </summary>
    [TestMethod()]
    public void AdditionalFields_CaseSensitiveKeys_PreserveBothVariants() {
      TestAdditionalFieldsContract item = new TestAdditionalFieldsContract();

      item.SetField("Meta", 1);
      item.SetField("meta", 2);

      Assert.AreEqual(1, item.GetField<int>("Meta"));
      Assert.AreEqual(2, item.GetField<int>("meta"));
      Assert.ThrowsException<AmbiguousMatchException>(() => item.GetField<int>("META"));
    }

    /// <summary>
    /// Verifies strict typed reads and null semantics.
    /// </summary>
    [TestMethod()]
    public void AdditionalFields_TypedReads_ConvertOrFailLoudly() {
      TestAdditionalFieldsContract item = new TestAdditionalFieldsContract();

      item.SetField("IntText", "42");
      item.SetField("Fraction", 1.5);
      item.SetField<object>("NullValue", null);

      Assert.AreEqual(42, item.GetField<int>("IntText"));
      Assert.AreEqual(123, item.GetField<int>("Missing", 123));
      Assert.IsNull(item.GetField<int?>("NullValue"));
      Assert.ThrowsException<InvalidCastException>(() => item.GetField<int>("Fraction"));
      Assert.ThrowsException<InvalidCastException>(() => item.GetField<int>("NullValue"));
    }

    /// <summary>
    /// Verifies container deep-copy normalization and DOM normalization.
    /// </summary>
    [TestMethod()]
    public void AdditionalFields_Containers_AreDeepCopiedAndDomFree() {
      TestAdditionalFieldsContract item = new TestAdditionalFieldsContract();
      Dictionary<string, object> nested = new Dictionary<string, object>(StringComparer.Ordinal) {
        { "Number", 7 },
        { "Text", "original" }
      };
      Dictionary<string, object> assigned = new Dictionary<string, object>(StringComparer.Ordinal) {
        { "Nested", nested },
        { "Array", new object[] { 1, "two" } },
        { "FromJObject", JsonConvert.DeserializeObject<object>("{\"Value\":5}") }
      };

      item.AdditionalFields = assigned;
      nested["Number"] = 9;

      Dictionary<string, object> storedNested = item.GetField<Dictionary<string, object>>("Nested");
      List<object> storedArray = item.GetField<List<object>>("Array");
      Dictionary<string, object> storedObject = item.GetField<Dictionary<string, object>>("FromJObject");

      Assert.AreEqual(7L, storedNested["Number"]);
      Assert.AreEqual(1L, storedArray[0]);
      Assert.AreEqual(5L, storedObject["Value"]);
      AssertDoesNotContainSerializerDom(item.AdditionalFields);
    }

    /// <summary>
    /// Creates a representative test contract.
    /// </summary>
    private static TestAdditionalFieldsContract CreateFilledContract() {
      TestAdditionalFieldsContract item = new TestAdditionalFieldsContract();
      item.Title = "Known title";
      item.SetField("AuthorShoeSize", 44);
      item.SetField("AuthorBirthday", new DateTime(1984, 10, 18));
      item.SetField("AnotherUuid", Guid.Parse("3C668CCB-AC1A-D9AE-49A0-7E19623A58D2"));
      item.SetField("Nested", new Dictionary<string, object>(StringComparer.Ordinal) {
        { "Child", "value" },
        { "Count", 3 }
      });
      return item;
    }

    /// <summary>
    /// Asserts that canonical extension data contains no serializer-specific DOM values.
    /// </summary>
    private static void AssertDoesNotContainSerializerDom(object value) {
      Assert.IsFalse(value is Newtonsoft.Json.Linq.JToken);
      Assert.IsFalse(value is JsonElement);

      IDictionary<string, object> dictionary = value as IDictionary<string, object>;
      if (dictionary != null) {
        foreach (KeyValuePair<string, object> entry in dictionary) {
          AssertDoesNotContainSerializerDom(entry.Value);
        }
        return;
      }

      IEnumerable<object> sequence = value as IEnumerable<object>;
      if (sequence != null) {
        foreach (object item in sequence) {
          AssertDoesNotContainSerializerDom(item);
        }
      }
    }

  }

  public partial class TestAdditionalFieldsContract {

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private SmartExtensionBag _AdditionalFields = null;

    private string _Title = "Untitled";

    public string Title {
      get {
        return _Title;
      }
      set {
        _Title = value;
      }
    }

    [Newtonsoft.Json.JsonExtensionData]
    [System.Text.Json.Serialization.JsonExtensionData]
    public IDictionary<string, object> AdditionalFields {
      get {
        return this.ExtensionBag;
      }
      set {
        _AdditionalFields = SmartExtensionBag.Adopt(this, value);
      }
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private SmartExtensionBag ExtensionBag {
      get {
        if (_AdditionalFields == null) {
          _AdditionalFields = new SmartExtensionBag(this);
        }
        return _AdditionalFields;
      }
    }

    public object this[string fieldName] {
      get {
        return this.GetField<object>(fieldName, null);
      }
      set {
        this.SetField(fieldName, value);
      }
    }

    /// <summary>
    /// Reads a typed field from known properties or extension data.
    /// </summary>
    public TValue GetField<TValue>(string fieldName, TValue defaultValue = default(TValue)) {
      object value;
      if (this.ExtensionBag.TryGetField(fieldName, typeof(TValue), out value)) {
        return (TValue)value;
      }
      return defaultValue;
    }

    /// <summary>
    /// Writes a typed field to known properties or extension data.
    /// </summary>
    public void SetField<TValue>(string fieldName, TValue value) {
      this.ExtensionBag[fieldName] = value;
    }

    }

}
