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
    private BoardSource[] _Sources = new BoardSource[0];
    private OriginTransition[] _AllowedOriginTransitions = new OriginTransition[0];

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

    /// <summary>
    /// The sources of a composed board (area root + origin); empty for single-source boards.
    /// </summary>
    public BoardSource[] Sources {
      get {
        return _Sources;
      }
      set {
        _Sources = value;
      }
    }

    /// <summary>
    /// The explicitly enabled (directed) transitions for moving cards between origins.
    /// </summary>
    public OriginTransition[] AllowedOriginTransitions {
      get {
        return _AllowedOriginTransitions;
      }
      set {
        _AllowedOriginTransitions = value;
      }
    }

  }

}
