using System;

namespace Collaboration.WorkTracking {

#if NET5_0_OR_GREATER
  //GRAD KAPUTT - PACKET GEHT NOCH NICHT FÜR .NET 48!

  public interface IWorkItemRepository : System.Data.Fuse.IRepository<WorkItem, WorkItemIdentity> {
  }

#endif

  public static class WorkItemRepositoryExtensions {

    //public static bool TryGetEntityByXXXXXX(
    //  this IRepository<WorkItem, long> repository, out string environment
    //) {

    //}

  }

}
