using System;

namespace Collaboration.WorkTracking {

  public class WorkItemIdentity {

    /// <summary>
    /// A concrete store id. Can also be the url of an online store like:
    /// https://dev.azure.com/MyOrganization/
    /// Note: this should only address the mandatory root which is absolutely
    /// fixed for your connecton credentials. Any sub-stores like 'TeamProjects'
    /// e.g. different Boards should not be part of the origin (either use the 'StorageArea')
    /// </summary>
    public string Origin { get; set; }

    /// <summary>
    /// Represents the internal, origin-scoped record id of the workitem
    /// </summary>
    public string OriginRelatedId { get; set; }

    public static WorkItemIdentity FromModel(WorkItem item) {
      return new WorkItemIdentity { Origin = item.Origin, OriginRelatedId = item.OriginRelatedId };
    }

  }

  public partial class WorkItem : WorkItemIdentity {

    #region " Workitem Type, Location & Order "

    /// <summary> 
    /// The Workitem-Class 'Generic' or provider related types like:
    /// 'AzureDevOps.Bug' | 'Github.Issue' | 'Trello.Card' | 'Outlook.Task'
    /// which can also be used to select different ui editors related to specialized data formats
    /// inside of the BodyContent field.
    /// </summary>
    public string Class { get; set; } = "Generic.WorkItem";

    /// <summary>
    /// Represents a area path ('/teamX/workspaceY/') to address a concrere sub-store inside of the origin.
    /// A sub-store can be something like a 'TeamProject' or a concrete 'Workspace'.
    /// NOTE: the set of possible values is NOT dynamic.
    /// </summary>
    public string StorageArea { get; set; } = "/";

    /// <summary>
    /// Represents a area path ('/diagnostics/monitoring/') to address a business-releated concern.
    /// The set of possible values is dynamic (areas will be created on-demand).
    /// </summary>
    public string ConcernArea { get; set; } = "/";

    /// <summary>
    /// A Priority-Value, dedicated for manual re-ordering by the user.
    /// ### Card-Reordering-Algorithm:
    /// (RANGE):
    ///   colums will display highest current number of the 'Snowflake44' algorithm, desc. to 0
    /// (DEFAULT) :
    ///   new items will placed on top, getting an initial value using the 'Snowflake44' algorithm
    /// (MOVEMENT):
    ///   manual movement of items will calculate a new value which is exactly in the middle between the
    ///   two values of the next higher and the next lower item.
    ///   * placing an item at the top of a column will use a new 'Snowflake44' value representing the 'higher' side
    ///   * placing an item at the bottom of a column will use 0 representing the 'lower' side
    /// (COLLISIONS):
    ///   collisions (no values left between upper and higher) will require updating the values of surrounding items: 
    ///   the lower item will be included in a calulation which will use the value of the next lower item, dividing
    ///   the possible range/3, if there are still collisions, it will proceed with 3 items within range/4 ...
    /// </summary>
    public long ManualReorderedPriority { get; set; } = 0;

    /// <summary>
    /// OPTIONAL: can refer to a (origin-related) recurrence-ruleset.
    /// If this value is not empty, the ui can offer tranparency about this.
    /// </summary>
    public string RecurrenceId { get; set; } = "";

    #endregion

    public string Title { get; set; } = "Untitled";

    /// <summary>
    /// Usually the 'Description', a long text, describing the todos
    /// (SEE ALSO: BodyContentMimetype)
    /// </summary>
    public string BodyContent { get; set; } = "Untitled";

    /// <summary>
    /// OPTIONAL: Gives a hint about a markup standard (if used) for the BodyContent,
    /// using mimetype-names. Can be for example:
    /// "text/markdown" | "text/html" | "application/json" | "application/xml" | "text/wiki".
    /// Binary mimetypes (such as images) needs an encoding information (as known from html-inline-data-urls):
    /// "image/png;base64" (RFC-2397).
    /// (default ""="text/plain")
    /// </summary>
    public string BodyContentMimetype { get; set; } = "text/plain";

    /// <summary>
    /// User-Defined tags.
    /// </summary>
    public string[] Tags { get; set; } = new string[] { };

    #region " Timestamps and Authors "

    /// <summary>
    /// The Date/Time when a user created the worktiem as utc UNIX-TIMESTAMP!
    /// (independend from any technical sync-operations)
    /// </summary>
    public long CreatedTimestamp { get; set; } = 0;

    /// <summary>
    /// The User whichs interaction created the workitem
    /// (independend from any technical sync-operations).
    /// This can be just a label 'John Doe' or an origin-related identifier using this syntax:
    /// 'John Does User-Label &lt;JOHN_DOES_USER_ID&gt;' (where the id could be his email-address)
    /// </summary>
    public string CreatedBy { get; set; } = "Unknown";

    /// <summary>
    /// The Date/Time when a user modified the workitem as utc UNIX-TIMESTAMP!
    /// (independend from any technical sync-operations).
    /// </summary>
    public long ModifiedTimestamp { get; set; } = 0;

    /// <summary>
    /// The User whichs interaction changed the Last-Modified Date/Time
    /// (independend from any technical sync-operations).
    /// This can be just a label 'John Doe' or an origin-related identifier using this syntax:
    /// 'John Does User-Label &lt;JOHN_DOES_USER_ID&gt;' (where the id could be his email-address)
    /// </summary>
    public string ModifiedBy { get; set; } = "Unknown";

    /// <summary>
    /// The Date/Time when a user marked the workitem for deletion as utc UNIX-TIMESTAMP!
    /// (independend from any technical sync-operations).
    /// The default value 0 is a magic-value, representing the semantic, 
    /// that the workitem is NOT YET marked for deletion
    /// </summary>
    public long DeletedTimestamp { get; set; } = 0;

    /// <summary>
    /// The User whichs interaction marked the workitem for deletion
    /// (independend from any technical sync-operations).
    /// This can be just a label 'John Doe' or an origin-related identifier using this syntax:
    /// 'John Does User-Label &lt;JOHN_DOES_USER_ID&gt;' (where the id could be his email-address)
    /// </summary>
    public string DeletedBy { get; set; } = "";

    #endregion

    #region " States of Work, Progress/Efforts, Planning-Deadlines "

    /// <summary>
    /// The User which is assigned to be responsible for the current ProgressState.
    /// This can be just a label 'John Doe' or an origin-related identifier using this syntax:
    /// 'John Does User-Label &lt;JOHN_DOES_USER_ID&gt;' (where the id could be his email-address)
    /// </summary>
    public string AssignedTo { get; set; } = "";

    /// <summary>
    /// Must be one of the Wellknown root-States: 
    /// "New", "Suspended"|"Working", "Done"|"Discontinued" (controlled semantic),
    /// but can be extended with individual sub-State names like for Example:
    /// "Suspended.Deferred", "Suspended.KnownIssue", "Suspended.AwaitingExternalResponse", "Working.Modelling"
    /// "Working.Implementation", "Working.UAT", "Done.Implemented", "Discontinued.WontFix". 
    /// Where any UI shoud have the correct behaviour related to the semantic-state,
    /// but display only the name of the sub-state!
    /// </summary>
    public string ProgressState { get; set; } = "New";

    /// <summary>
    /// The Date/Time when the ProgressState moved to its current value as utc UNIX-TIMESTAMP!
    /// </summary>
    public long ProgressStateTimestamp { get; set; } = 0;

    /// <summary>
    /// OPTIONAL: a quick note related to the current ProgressState.
    /// Must be in plaintext without linebreaks and a max-length of 1000.
    /// </summary>
    public string ProgressNote { get; set; } = "";

    /// <summary>
    /// The currently reached effort
    /// (related to the: EffortUnit).
    /// </summary>
    public int ProgressEffort { get; set; } = 0;

    /// <summary>
    /// The estimated effort until completion of the workitem
    /// (related to the: EffortUnit).
    /// </summary>
    public int EstimatedEffort { get; set; } = 0;

    /// <summary>
    /// The unit of the values in ProgressEffort and EstimatedEffort.
    /// Must be one of the following: "hours" | "days" | "Sizes" (1='XS',2='S',3='L',4='XL',5="XXL") | "steps" (for time-independent work packages)
    /// </summary>
    public string EffortUnit { get; set; } = "";

    /// <summary>
    /// The Date/Time of the first transition to a 'Working'-State
    /// (can also be a planned future-date) as utc UNIX-TIMESTAMP!
    /// </summary>
    public long StartTimestamp { get; set; } = 0;

    /// <summary>
    /// The estimated Date/Time when a 'Done'-State should be reached as utc UNIX-TIMESTAMP!
    /// </summary>
    public long DeadlineTimestamp { get; set; } = 0;

    #endregion

    /// <summary>
    /// Contains Links to WorkItems (Paren/Related/..), Attachments (Files/Hyperlinks),
    /// SystemData (like the AuditTrail or UserComments etc.) or 'Checklist-Entries' (if supported) 
    /// </summary>
    public LinkedItem[] LinkedItems { get; set; } = new LinkedItem[] { };

  }
  
  public class LinkedItem  {

    /// <summary>
    /// Must be one of the Wellknown values: 
    /// "Parent"|"Related"|"Child"|"Attachment"|"Hyperlink"|"Checkitem"|"SystemData" (controlled semantic),
    /// but can be extended with individual classfication names like for Example:
    /// "Related.Predecessor", "Related.Successor", "Related.DuplicateOf". 
    /// Where any UI shoud have the correct behaviour related to the semantic-linktype,
    /// but display only the classfication name!
    /// </summary>
    public string LinkType { get; set; } = "Related";

    /// <summary>
    /// For LinkTypes "Parent","Related","Child" this is the WorkItems Origin as we know it from the WorkItem-Model (see description) of the target Workitem.
    /// For LinkType "Attachment" this is the file-title.
    /// For LinkType "Hyperlink" this is just a display-label.
    /// For LinkType "Checkitem" this is the name of a checklist owned by the current workitem (""=default).
    /// For LinkType "SystemData" this is onw of the following container-names: "AuditTrail"|"UserComments". 
    /// </summary>
    public string Origin { get; set; }

    /// <summary>
    /// For LinkTypes "Parent","Related","Child" this is the WorkItems record id as we know it from the WorkItem-Model (see description) of the target Workitem.
    /// For LinkType "Attachment" this is the binary content as we know it from data-urls 'application/pdf;base64:xxxxx...' (RFC-2397)
    /// For LinkType "Hyperlink" this is the url.
    /// For LinkType "Checkitem" this is a string like: "[ ] My Task 1" or "[x] My Task 2".
    /// For LinkType "SystemData" this is a json-structure containen large addon-data ("AuditTrail", "UserComments", etc.)
    /// </summary>
    public string Target { get; set; }

    /******************************************************************
     
      Sample for SystemData -> AuditTrail:     
      { DICT
        383848485355(timestamp): {
          "by": "John Dow <john.doe@cool.org>",
          "changes": [
            {
              "fieldname": "Title",
              "oldValue": "A",
              "newValue": "B"
            }
          ],
          "userNote" (optional): "i changed title: \n from 'A' to 'b'"
        }
      }

      Sample for SystemData -> UserComments:     
      { DICT
        383848485355(timestamp): {
          "by": "John Dow <john.doe@cool.org>",
          "comment": "i changed title: \n from 'A' to 'b'",
          "isPrivateNote": true
        }
      }

     ********************************************************************/

  }

}
