document.addEventListener('DOMContentLoaded', async () => {
    try {
        const el = document.getElementById('adminStatsChart');
        if (!el) return;
        const res = await fetch('/Admin/Stats/Data');
        if (!res.ok) return;
        const data = await res.json();
        const labels = data.labels || [];
        const docCounts = data.docCounts || [];
        const templateCounts = data.templateCounts || [];
        const ctx = el.getContext('2d');
        new Chart(ctx, {
            type: 'line',
            data: {
                labels: labels,
                datasets: [
                    {
                        label: 'Documents',
                        data: docCounts,
                        borderColor: 'rgba(54, 162, 235, 1)',
                        backgroundColor: 'rgba(54, 162, 235, 0.14)',
                        tension: 0.2
                    },
                    {
                        label: 'Templates',
                        data: templateCounts,
                        borderColor: 'rgba(153,102,255,1)',
                        backgroundColor: 'rgba(153,102,255,0.12)',
                        tension: 0.2
                    }
                ]
            },
            options: {
                responsive: true,
                interaction: {
                    mode: 'index',
                    intersect: false
                },
                scales: {
                    x: { display: true },
                    y: { display: true, beginAtZero: true }
                }
            }
        });
    }
    catch (e) {
        console.error('Failed to load admin stats', e);
    }
});
