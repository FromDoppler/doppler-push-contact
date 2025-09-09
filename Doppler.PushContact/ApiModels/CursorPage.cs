using System.Collections.Generic;

namespace Doppler.PushContact.ApiModels
{
    public class CursorPage<T>
    {
        public List<T> Items { get; }

        /// <summary>
        /// Cursor to use in next query (null when there are no results).
        /// </summary>
        public string NextCursor { get; }

        public int PerPage { get; }

        public CursorPage(List<T> items, string nextCursor, int perPage)
        {
            Items = items ?? new List<T>();
            NextCursor = nextCursor;
            PerPage = perPage;
        }
    }
}
