using System.Collections.Generic;

namespace Renumber.Services
{
    /// <summary>
    /// Session-scoped registry tracking which DALI view filters the tool has applied
    /// or modified in each view. Used to safely reset overrides without affecting
    /// user-created filters.
    ///
    /// Thread safety: All access occurs on the Revit API thread or the WPF UI thread
    /// (never concurrently), so explicit locking is not required. If this changes,
    /// replace Dictionary with ConcurrentDictionary.
    ///
    /// Lifetime: Singleton per session. Created once when the plugin window opens.
    /// Cleared when the plugin closes or the user explicitly resets.
    /// </summary>
    public class HighlightRegistry
    {
        /// <summary>
        /// Maps View ElementId (as long) -> set of ParameterFilterElement ElementIds (as long)
        /// that this tool applied or modified.
        /// Using long keys for cross-framework compatibility (net48 IntegerValue, net8 Value).
        /// </summary>
        private readonly Dictionary<long, HashSet<long>> _viewFilterMap
            = new Dictionary<long, HashSet<long>>();

        /// <summary>
        /// Records that the tool applied/modified a filter in a view.
        /// </summary>
        public void Track(long viewIdValue, long filterIdValue)
        {
            if (!_viewFilterMap.TryGetValue(viewIdValue, out var filterSet))
            {
                filterSet = new HashSet<long>();
                _viewFilterMap[viewIdValue] = filterSet;
            }
            filterSet.Add(filterIdValue);
        }

        /// <summary>
        /// Returns all tracked filter IDs for a specific view.
        /// Returns an empty set if the view has no tracked filters.
        /// </summary>
        public IReadOnlyCollection<long> GetFiltersForView(long viewIdValue)
        {
            if (_viewFilterMap.TryGetValue(viewIdValue, out var filterSet))
            {
                return filterSet;
            }
            return new long[0];
        }

        /// <summary>
        /// Returns all view IDs that have tracked filters.
        /// </summary>
        public IEnumerable<long> GetAllTrackedViewIds()
        {
            return _viewFilterMap.Keys;
        }

        /// <summary>
        /// Removes tracking entries for a specific view (after overrides are cleared).
        /// </summary>
        public void ClearView(long viewIdValue)
        {
            _viewFilterMap.Remove(viewIdValue);
        }

        /// <summary>
        /// Removes all tracking data (full session reset).
        /// </summary>
        public void ClearAll()
        {
            _viewFilterMap.Clear();
        }

        /// <summary>
        /// Returns true if any filters are tracked in any view.
        /// </summary>
        public bool HasAnyTrackedFilters
        {
            get
            {
                foreach (var kvp in _viewFilterMap)
                {
                    if (kvp.Value.Count > 0) return true;
                }
                return false;
            }
        }
    }
}
