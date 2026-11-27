namespace Collaboration.WorkTracking.Boards {

  /// <summary>
  /// Optionally implemented by composed board repositories to publish their sources and the explicitly
  /// enabled (directed) origin transitions to the board UI.
  /// </summary>
  public interface IBoardSourceInfo {

    /// <summary>
    /// Returns the area root and origin of every source of the board.
    /// </summary>
    BoardSource[] GetBoardSources();

    /// <summary>
    /// Returns all explicitly enabled (directed) origin transitions.
    /// </summary>
    OriginTransition[] GetAllowedOriginTransitions();

  }

  /// <summary>
  /// A source of a composed board: its area root ('/repo-alpha') and the 'Origin' of its cards.
  /// </summary>
  public class BoardSource {

    private string _AreaRoot = string.Empty;
    private string _Origin = string.Empty;

    public BoardSource() {
    }

    public BoardSource(string areaRoot, string origin) {
      _AreaRoot = areaRoot;
      _Origin = origin;
    }

    /// <summary>
    /// Root segment of 'ConcernArea' including the leading slash, e.g. '/repo-alpha'.
    /// </summary>
    public string AreaRoot {
      get {
        return _AreaRoot;
      }
      set {
        _AreaRoot = value;
      }
    }

    /// <summary>
    /// The 'Origin' of all cards provided by this source.
    /// </summary>
    public string Origin {
      get {
        return _Origin;
      }
      set {
        _Origin = value;
      }
    }

  }

  /// <summary>
  /// A DIRECTED, explicitly enabled transition which allows moving cards from one origin (source) to another.
  /// </summary>
  public class OriginTransition {

    private string _SourceOrigin = string.Empty;
    private string _TargetOrigin = string.Empty;

    public OriginTransition() {
    }

    public OriginTransition(string sourceOrigin, string targetOrigin) {
      _SourceOrigin = sourceOrigin;
      _TargetOrigin = targetOrigin;
    }

    /// <summary>
    /// The origin the card is moved away from.
    /// </summary>
    public string SourceOrigin {
      get {
        return _SourceOrigin;
      }
      set {
        _SourceOrigin = value;
      }
    }

    /// <summary>
    /// The origin the card is moved to.
    /// </summary>
    public string TargetOrigin {
      get {
        return _TargetOrigin;
      }
      set {
        _TargetOrigin = value;
      }
    }

  }

}
