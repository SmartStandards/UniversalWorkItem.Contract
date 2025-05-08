using System;
using System.Data.Fuse;

namespace Collaboration.WorkTracking {

  public interface IWorkItemRepository : IRepository<WorkItem, WorkItemIdentity> {
  }

  public static class WorkItemRepositoryExtensions {

    //public static bool TryGetEntityByXXXXXX(
    //  this IRepository<WorkItem, long> repository, out string environment
    //) {

    //}

  }

}
