using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Collaboration.WorkTracking {

  [TestClass]
  public class Tests {

    [TestMethod()]
    public void SerializationTest_Newtonsoft() {

      WorkItem sourceItem = new WorkItem();

      sourceItem.Title = "Foo";
      sourceItem.BodyContent = "Some details about a huge Foo!";
      sourceItem.CreatedBy = "Me!";

      DateTime creationDate = DateTime.Now;
      sourceItem.SetCreatedTimestamp(creationDate);

      sourceItem.Tags = new string[] { "IsCool", "IsCrazy" };

      sourceItem.SetCustomField("AuthorShoeSize", 44);
      sourceItem.SetCustomField("AuthorBirthday", DateTime.Parse("1984-10-18"));
      sourceItem.SetCustomField("AnotherUuid", Guid.Parse("3C668CCB-AC1A-D9AE-49A0-7E19623A58D2"));

      /////////////////////////////////////////////////////////////////////////////////////////////
      //Raw Dictionary-Items for the custom Fields

      Assert.AreEqual(44, sourceItem["AuthorShoeSize"]);
      Assert.AreEqual("int32", sourceItem["AuthorShoeSize`T"]);

      Assert.AreEqual(DateTime.Parse("1984-10-18"), sourceItem["AuthorBirthday"]);
      Assert.AreEqual("datetime", sourceItem["AuthorBirthday`T"]);

      Assert.AreEqual(Guid.Parse("3C668CCB-AC1A-D9AE-49A0-7E19623A58D2"), sourceItem["AnotherUuid"]);
      Assert.AreEqual("uuid", sourceItem["AnotherUuid`T"]);

      /////////////////////////////////////////////////////////////////////////////////////////////
      // Serialization & Deserialization

      string serializedItemAsJson = JsonConvert.SerializeObject(sourceItem, Formatting.Indented);

      WorkItem deserializedItem = JsonConvert.DeserializeObject<WorkItem>(serializedItemAsJson);

      /////////////////////////////////////////////////////////////////////////////////////////////

      Assert.AreEqual(sourceItem.Title, deserializedItem.Title);
      Assert.AreEqual(sourceItem.BodyContent, deserializedItem.BodyContent);
      Assert.AreEqual(sourceItem.CreatedBy, deserializedItem.CreatedBy);

      Assert.AreEqual(sourceItem.CreatedTimestamp, deserializedItem.CreatedTimestamp);
      Assert.AreEqual(creationDate, deserializedItem.GetCreatedTimestampAsDate());

      Assert.AreEqual(sourceItem.Tags.Length, deserializedItem.Tags.Length);
      Assert.AreEqual(sourceItem.Tags[0], deserializedItem.Tags[0]);
      Assert.AreEqual(sourceItem.Tags[1], deserializedItem.Tags[1]);

      //Raw Dictionary-Items for the custom Fields
      Assert.AreEqual(44, deserializedItem["AuthorShoeSize"]);
      Assert.AreEqual("1984-10-18", deserializedItem["AuthorBirthday"]);
      Assert.AreEqual("3C668CCB-AC1A-D9AE-49A0-7E19623A58D2", deserializedItem["AnotherUuid"]);

      //custom Fields
      Assert.AreEqual(44, deserializedItem.GetCustomField("AuthorShoeSize",0));
      Assert.AreEqual(DateTime.Parse("1984-10-18"), deserializedItem.GetCustomField("AuthorBirthday",DateTime.MinValue));
      Assert.AreEqual(Guid.Parse("3C668CCB-AC1A-D9AE-49A0-7E19623A58D2"), deserializedItem.GetCustomField("AnotherUuid",Guid.Empty));

    }

  }

}
