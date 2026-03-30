document.addEventListener('DOMContentLoaded', async () => {
    try {
        const res = await fetch('/Admin/Stats/Data');
        if (!res.ok) return;
        const data = await res.json();

        const makeSparkline = (canvasId, values, color) => {
            const el = document.getElementById(canvasId);
            if (!el) return;
            const ctx = el.getContext('2d');
            new Chart(ctx, {
                type: 'line',
                data: {
                    labels: data.labels || [],
                    datasets: [{
                        data: values || [],
                        borderColor: color,
                        backgroundColor: 'rgba(0,0,0,0)',
                        tension: 0.35,
                        pointRadius: 0,
                        borderWidth: 2
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: { legend: { display: false } },
                    scales: {
                        x: { display: false },
                        y: { display: false, beginAtZero: true }
                    },
                    elements: { line: { capBezierPoints: true } }
                }
            });
        };

        makeSparkline('statChartDocs', data.docCounts || [], 'rgba(54,162,235,1)');
        makeSparkline('statChartTemplates', data.templateCounts || [], 'rgba(153,102,255,1)');
        // Users: no time-series in StatsData; show docCounts as a proxy small chart if available
        makeSparkline('statChartUsers', data.docCounts || [], 'rgba(75,192,192,1)');

    } catch (e) {
        console.error('admin-dashboard-widgets failed', e);
    }
});
