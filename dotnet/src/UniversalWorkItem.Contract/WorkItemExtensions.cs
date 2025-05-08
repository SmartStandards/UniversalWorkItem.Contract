using System;

namespace Collaboration.WorkTracking {

  public static class WorkItemExtensions {

    private static DateTime _UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

    #region " StartTimestamp (DateTime<>UnixTimestamp) "

    /// <summary>
    /// Converts the (unix-)timestamp into a (local) DateTime or returns DateTime.MinValue if 0.
    /// </summary>
    /// <param name="workItem"></param>
    /// <param name="timestamp"></param>
    /// <returns></returns>
    public static DateTime GetStartTimestampAsDate(this WorkItem workItem) {
      if(workItem.StartTimestamp <= 0) {
        return DateTime.MinValue;
      }
      else {
        return _UnixEpoch.AddSeconds(workItem.StartTimestamp).ToLocalTime();
      }
    }
    /// <summary>
    /// Converts a given DateTime into (unix-)timestamp while setting the property. 
    /// </summary>
    /// <param name="workItem"></param>
    /// <param name="timestamp"></param>
    public static void SetStartTimestamp(this WorkItem workItem, DateTime timestamp) {   
      if(timestamp < _UnixEpoch) {
        workItem.StartTimestamp = 0;
      }
      else {
        TimeSpan spanSinceUnixEpoch = (timestamp.ToUniversalTime() - _UnixEpoch);
        workItem.StartTimestamp = Convert.ToInt64(spanSinceUnixEpoch.TotalSeconds);
      }
    }

    #endregion

    #region " CreatedTimestamp (DateTime<>UnixTimestamp) "

    /// <summary>
    /// Converts the (unix-)timestamp into a (local) DateTime or returns DateTime.MinValue if 0.
    /// </summary>
    /// <param name="workItem"></param>
    /// <param name="timestamp"></param>
    /// <returns></returns>
    public static DateTime GetCreatedTimestampAsDate(this WorkItem workItem) {
      if (workItem.CreatedTimestamp <= 0) {
        return DateTime.MinValue;
      }
      else {
        return _UnixEpoch.AddSeconds(workItem.CreatedTimestamp).ToLocalTime();
      }
    }
    /// <summary>
    /// Converts a given DateTime into (unix-)timestamp while setting the property. 
    /// </summary>
    /// <param name="workItem"></param>
    /// <param name="timestamp"></param>
    public static void SetCreatedTimestamp(this WorkItem workItem, DateTime timestamp) {
      if (timestamp < _UnixEpoch) {
        workItem.CreatedTimestamp = 0;
      }
      else {
        TimeSpan spanSinceUnixEpoch = (timestamp.ToUniversalTime() - _UnixEpoch);
        workItem.CreatedTimestamp = Convert.ToInt64(spanSinceUnixEpoch.TotalSeconds);
      }
    }

    #endregion

    #region " ModifiedTimestamp (DateTime<>UnixTimestamp) "

    /// <summary>
    /// Converts the (unix-)timestamp into a (local) DateTime or returns DateTime.MinValue if 0.
    /// </summary>
    /// <param name="workItem"></param>
    /// <param name="timestamp"></param>
    /// <returns></returns>
    public static DateTime GetModifiedTimestampAsDate(this WorkItem workItem) {
      if (workItem.ModifiedTimestamp <= 0) {
        return DateTime.MinValue;
      }
      else {
        return _UnixEpoch.AddSeconds(workItem.ModifiedTimestamp).ToLocalTime();
      }
    }
    /// <summary>
    /// Converts a given DateTime into (unix-)timestamp while setting the property. 
    /// </summary>
    /// <param name="workItem"></param>
    /// <param name="timestamp"></param>
    public static void SetModifiedTimestamp(this WorkItem workItem, DateTime timestamp) {
      if (timestamp < _UnixEpoch) {
        workItem.ModifiedTimestamp = 0;
      }
      else {
        TimeSpan spanSinceUnixEpoch = (timestamp.ToUniversalTime() - _UnixEpoch);
        workItem.ModifiedTimestamp = Convert.ToInt64(spanSinceUnixEpoch.TotalSeconds);
      }
    }

    #endregion

    #region " DeletedTimestamp (DateTime<>UnixTimestamp) "

    /// <summary>
    /// Converts the (unix-)timestamp into a (local) DateTime or returns DateTime.MinValue if 0.
    /// </summary>
    /// <param name="workItem"></param>
    /// <param name="timestamp"></param>
    /// <returns></returns>
    public static DateTime GetDeletedTimestampAsDate(this WorkItem workItem) {
      if (workItem.DeletedTimestamp <= 0) {
        return DateTime.MinValue;
      }
      else {
        return _UnixEpoch.AddSeconds(workItem.DeletedTimestamp).ToLocalTime();
      }
    }
    /// <summary>
    /// Converts a given DateTime into (unix-)timestamp while setting the property. 
    /// </summary>
    /// <param name="workItem"></param>
    /// <param name="timestamp"></param>
    public static void SetDeletedTimestamp(this WorkItem workItem, DateTime timestamp) {
      if (timestamp < _UnixEpoch) {
        workItem.DeletedTimestamp = 0;
      }
      else {
        TimeSpan spanSinceUnixEpoch = (timestamp.ToUniversalTime() - _UnixEpoch);
        workItem.DeletedTimestamp = Convert.ToInt64(spanSinceUnixEpoch.TotalSeconds);
      }
    }

    #endregion

    #region " DeadlineTimestamp (DateTime<>UnixTimestamp) "

    /// <summary>
    /// Converts the (unix-)timestamp into a (local) DateTime or returns DateTime.MinValue if 0.
    /// </summary>
    /// <param name="workItem"></param>
    /// <param name="timestamp"></param>
    /// <returns></returns>
    public static DateTime GetDeadlineTimestampAsDate(this WorkItem workItem) {
      if (workItem.DeadlineTimestamp <= 0) {
        return DateTime.MinValue;
      }
      else {
        return _UnixEpoch.AddSeconds(workItem.DeadlineTimestamp).ToLocalTime();
      }
    }
    /// <summary>
    /// Converts a given DateTime into (unix-)timestamp while setting the property. 
    /// </summary>
    /// <param name="workItem"></param>
    /// <param name="timestamp"></param>
    public static void SetDeadlineTimestamp(this WorkItem workItem, DateTime timestamp) {
      if (timestamp < _UnixEpoch) {
        workItem.DeadlineTimestamp = 0;
      }
      else {
        TimeSpan spanSinceUnixEpoch = (timestamp.ToUniversalTime() - _UnixEpoch);
        workItem.DeadlineTimestamp = Convert.ToInt64(spanSinceUnixEpoch.TotalSeconds);
      }
    }

    #endregion

    #region " ProgressStateTimestamp (DateTime<>UnixTimestamp) "

    /// <summary>
    /// Converts the (unix-)timestamp into a (local) DateTime or returns DateTime.MinValue if 0.
    /// </summary>
    /// <param name="workItem"></param>
    /// <param name="timestamp"></param>
    /// <returns></returns>
    public static DateTime GetProgressStateTimestampAsDate(this WorkItem workItem) {
      if (workItem.ProgressStateTimestamp <= 0) {
        return DateTime.MinValue;
      }
      else {
        return _UnixEpoch.AddSeconds(workItem.ProgressStateTimestamp).ToLocalTime();
      }
    }
    /// <summary>
    /// Converts a given DateTime into (unix-)timestamp while setting the property. 
    /// </summary>
    /// <param name="workItem"></param>
    /// <param name="timestamp"></param>
    public static void SetProgressStateTimestamp(this WorkItem workItem, DateTime timestamp) {
      if (timestamp < _UnixEpoch) {
        workItem.ProgressStateTimestamp = 0;
      }
      else {
        TimeSpan spanSinceUnixEpoch = (timestamp.ToUniversalTime() - _UnixEpoch);
        workItem.ProgressStateTimestamp = Convert.ToInt64(spanSinceUnixEpoch.TotalSeconds);
      }
    }

    #endregion

  }

}
