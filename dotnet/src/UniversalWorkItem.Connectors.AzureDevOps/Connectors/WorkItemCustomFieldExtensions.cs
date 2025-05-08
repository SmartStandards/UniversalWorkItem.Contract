using System;
using System.Collections.Generic;
using System.Data.Fuse;
using System.Runtime.CompilerServices;

namespace Collaboration.WorkTracking.Connectors.AzureDevOps {

  public static class WorkItemCustomFieldExtensions {

    public static AzureDevOpsRelatedFieldsAccessor AzureDevOpsRelatedFieldsAccesso(this WorkItem workItem) {
      return new AzureDevOpsRelatedFieldsAccessor(workItem);
    }

  }

  public class AzureDevOpsRelatedFieldsAccessor {

    WorkItem _WorkItem;
    public AzureDevOpsRelatedFieldsAccessor(WorkItem workItem) {
      _WorkItem = workItem;
    }

    public string IterationPath {
      get {
        return _WorkItem.GetCustomField(nameof(IterationPath), "/");
      }
      set {
        _WorkItem.SetCustomField(nameof(IterationPath), value);
      }
    }

  }

}
