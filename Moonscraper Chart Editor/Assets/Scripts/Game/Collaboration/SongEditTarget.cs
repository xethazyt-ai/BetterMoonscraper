// See LICENSE in project root for license information.

using System;
using MoonscraperChartEditor.Song;

/// <summary>
/// The chart that song edit commands read from and write to. Defaults to the chart currently open in the editor.
/// Commands remember the chart they were created against and override this while they invoke or revoke,
/// so an edit (or an undo of it) always lands in the chart it was made in, regardless of what's on screen.
/// </summary>
public static class SongEditTarget
{
    static Chart s_chartOverride = null;

    public static Chart chart
    {
        get { return s_chartOverride ?? ChartEditor.Instance.currentChart; }
    }

    public static Scope Use(Chart chart)
    {
        Scope scope = new Scope(s_chartOverride);
        s_chartOverride = chart;
        return scope;
    }

    public struct Scope : IDisposable
    {
        readonly Chart m_previous;

        public Scope(Chart previous)
        {
            m_previous = previous;
        }

        public void Dispose()
        {
            s_chartOverride = m_previous;
        }
    }
}
