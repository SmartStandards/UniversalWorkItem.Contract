using System;
using System.Collections.Generic;
using System.Linq;

namespace Collaboration.WorkTracking.Boards {

  /// <summary>
  /// Holds the composed WorkItem repository of every board, addressed by its board id.
  /// This is the MVP (in-memory) registry; it will later be fed from the persisted board configuration.
  /// </summary>
  public class BoardRegistry {

    private readonly Dictionary<string, BoardRegistration> _Boards = new Dictionary<string, BoardRegistration>(
      StringComparer.OrdinalIgnoreCase
    );

    /// <summary>
    /// Registers (or replaces) the board with the given id.
    /// </summary>
    public void Register(string boardId, string title, bool isReadOnly, IWorkItemRepository repository) {

      if (string.IsNullOrWhiteSpace(boardId)) {
        throw new ArgumentException("The board id must not be empty.", nameof(boardId));
      }

      if (repository == null) {
        throw new ArgumentNullException(nameof(repository));
      }

      BoardRegistration registration = new BoardRegistration(boardId, title, isReadOnly, repository);
      BoardRegistration replaced = null;

      lock (_Boards) {
        _Boards.TryGetValue(boardId, out replaced);
        _Boards[boardId] = registration;
      }

      DisposeRepository(replaced, repository);

    }

    /// <summary>
    /// Removes the board with the given id (its repository is disposed, if disposable).
    /// Returns false if there was no such board.
    /// </summary>
    public bool Unregister(string boardId) {

      if (string.IsNullOrWhiteSpace(boardId)) {
        return false;
      }

      BoardRegistration removed = null;

      lock (_Boards) {

        if (!_Boards.TryGetValue(boardId, out removed)) {
          return false;
        }

        _Boards.Remove(boardId);
      }

      DisposeRepository(removed, null);

      return true;
    }

    private static void DisposeRepository(BoardRegistration registration, IWorkItemRepository stillUsedRepository) {

      if (registration == null || ReferenceEquals(registration.Repository, stillUsedRepository)) {
        return;
      }

      IDisposable disposable = registration.Repository as IDisposable;

      if (disposable != null) {
        disposable.Dispose();
      }

    }

    /// <summary>
    /// Returns all registered boards ordered by title.
    /// </summary>
    public BoardRegistration[] GetBoards() {

      lock (_Boards) {
        return _Boards.Values.OrderBy((board) => board.Title).ToArray();
      }

    }

    /// <summary>
    /// Tries to resolve the board with the given id.
    /// </summary>
    public bool TryGetBoard(string boardId, out BoardRegistration registration) {

      registration = null;

      if (string.IsNullOrWhiteSpace(boardId)) {
        return false;
      }

      lock (_Boards) {
        return _Boards.TryGetValue(boardId, out registration);
      }

    }

  }

  /// <summary>
  /// A board as known by the <see cref="BoardRegistry"/>.
  /// </summary>
  public class BoardRegistration {

    private readonly string _BoardId;
    private readonly string _Title;
    private readonly bool _IsReadOnly;
    private readonly IWorkItemRepository _Repository;

    public BoardRegistration(string boardId, string title, bool isReadOnly, IWorkItemRepository repository) {
      _BoardId = boardId;
      _Title = title;
      _IsReadOnly = isReadOnly;
      _Repository = repository;
    }

    public string BoardId {
      get {
        return _BoardId;
      }
    }

    public string Title {
      get {
        return _Title;
      }
    }

    public bool IsReadOnly {
      get {
        return _IsReadOnly;
      }
    }

    public IWorkItemRepository Repository {
      get {
        return _Repository;
      }
    }

  }

}
