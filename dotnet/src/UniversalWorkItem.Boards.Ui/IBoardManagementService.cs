namespace Collaboration.WorkTracking.Boards {

  /// <summary>
  /// Board management (configuration and view state), exposed to the board UI via UJMW.
  /// Version 0: lists the available boards only. Views, area tree and field constraints follow (see epic).
  /// </summary>
  public interface IBoardManagementService {

    /// <summary>
    /// Lists the boards the caller may see.
    /// </summary>
    BoardDescriptor[] GetBoards();

  }

  /// <summary>
  /// Public description of a board (never exposes persistence entities).
  /// </summary>
  public class BoardDescriptor {

    private string _BoardId = string.Empty;
    private string _Title = string.Empty;
    private bool _IsReadOnly = false;

    /// <summary>
    /// Identifier of the board; used as last route segment of the board-specific WorkItem endpoint.
    /// </summary>
    public string BoardId {
      get {
        return _BoardId;
      }
      set {
        _BoardId = value;
      }
    }

    public string Title {
      get {
        return _Title;
      }
      set {
        _Title = value;
      }
    }

    public bool IsReadOnly {
      get {
        return _IsReadOnly;
      }
      set {
        _IsReadOnly = value;
      }
    }

  }

}
