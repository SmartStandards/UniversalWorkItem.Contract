
export class WorkItemIdentity {
  
  public Origin: string = "";
 
  public OriginRelatedId: string = "";
 
}

export class WorkItem extends WorkItemIdentity {
  
  public Class: string = "Generic.WorkItem";
  
  public StorageArea: string = "/";
  
  public ConcernArea: string = "/";
    
  public ManualReorderedPriority: number = 0;
 
  public Title: string = "Untitled";

  public BodyContent: string = "";

  //...

  public CreatedTimestamp : number = 0;

  //...

  public Tags: string[] = [];

  //...

}
