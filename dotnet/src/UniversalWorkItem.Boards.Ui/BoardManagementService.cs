using System;
using System.Collections.Generic;

namespace Collaboration.WorkTracking.Boards {

  /// <summary>
  /// MVP implementation of <see cref="IBoardManagementService"/> on top of the in-memory <see cref="BoardRegistry"/>.
  /// Will be replaced by an implementation backed by the EF/FUSE board DataStore.
  /// </summary>
  public class BoardManagementService : IBoardManagementService {

    private readonly BoardRegistry _Registry;

    public BoardManagementService(BoardRegistry registry) {

      if (registry == null) {
        throw new ArgumentNullException(nameof(registry));
      }

      _Registry = registry;
    }

    /// <summary>
    /// Lists all registered boards.
    /// </summary>
    public BoardDescriptor[] GetBoards() {

      List<BoardDescriptor> result = new List<BoardDescriptor>();

      foreach (BoardRegistration board in _Registry.GetBoards()) {

        BoardDescriptor descriptor = new BoardDescriptor();
        descriptor.BoardId = board.BoardId;
        descriptor.Title = board.Title;
        descriptor.IsReadOnly = board.IsReadOnly;

        IBoardSourceInfo sourceInfo = board.Repository as IBoardSourceInfo;

        if (sourceInfo != null) {
          descriptor.Sources = sourceInfo.GetBoardSources();
          descriptor.AllowedOriginTransitions = sourceInfo.GetAllowedOriginTransitions();
        }

        result.Add(descriptor);
      }

      return result.ToArray();
    }

  }

}
