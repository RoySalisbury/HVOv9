let chartJsPromise;
let chartModule;
let registerablesRegistered = false;
let chartJsFallbackPromise;
let chartJsCdnFailed = false;
let kurkleColorPromise;
const charts = new Map();

async function loadModule(url) {
    return import(/* @vite-ignore */ url);
}

function normalizeAssetPath(path) {
    if (!path || typeof path !== "string") {
        return path;
    }

    if (path.startsWith("/") || path.startsWith("./") || path.startsWith("../") || path.includes("://")) {
        return path;
    }

    return `/${path}`;
}

function getChartJsAssetPath() {
    const candidate = window.hvoChartAssets?.chart ?? "/lib/chartjs/chart.js";
    return normalizeAssetPath(candidate);
}

function getKurkleColorAssetPath() {
    const candidate = window.hvoChartAssets?.color ?? "/lib/chartjs/@kurkle/color.js";
    return normalizeAssetPath(candidate);
}

async function ensureChartJs() {
    if (!chartModule) {
        if (!chartJsCdnFailed) {
            if (!chartJsPromise) {
                chartJsPromise = loadModule("https://cdn.jsdelivr.net/npm/chart.js@4.4.4/dist/chart.js");
            }

            try {
                chartModule = await chartJsPromise;
            } catch (error) {
                console.warn("SkyMonitor diagnostics: failed to load Chart.js from CDN, falling back to local copy.", error);
                chartJsCdnFailed = true;
            }
        }

        if (!chartModule) {
            if (!chartJsFallbackPromise) {
                chartJsFallbackPromise = loadModule(getChartJsAssetPath());
            }

            try {
                chartModule = await chartJsFallbackPromise;
            } catch (fallbackError) {
                console.error("SkyMonitor diagnostics: failed to load the local Chart.js module.", fallbackError);
                throw fallbackError;
            }
        }
    }

    const { Chart, registerables } = chartModule;
    if (!registerablesRegistered) {
        let colorModule;
        try {
            colorModule = await loadModule(getKurkleColorAssetPath());
        }
        catch (colorError) {
            console.error("SkyMonitor diagnostics: failed to load @kurkle/color from local assets.", colorError);
            throw colorError;
        }
        Chart.register(...registerables);
        registerablesRegistered = true;
        Chart.defaults.color = "rgba(248, 249, 250, 0.75)";
        Chart.defaults.font.family = "'Inter', 'Segoe UI', 'Helvetica Neue', sans-serif";
        Chart.defaults.plugins.legend.display = false;
    }

    return chartModule;
}

function resolveKey(target) {
    if (!target) {
        return null;
    }

    if (typeof target === "string") {
        return target;
    }

    return target.id ?? null;
}

function formatValue(value, suffix) {
    if (typeof value !== "number" || Number.isNaN(value)) {
        return `0${suffix}`;
    }

    if (suffix.trim() === "%") {
        return `${value.toFixed(1)}${suffix}`;
    }

    if (Math.abs(value) >= 100) {
        return `${value.toFixed(0)}${suffix}`;
    }

    return `${value.toFixed(2)}${suffix}`;
}

export async function updateLineChart(canvas, data, options) {
    if (!canvas || typeof canvas.getContext !== "function" || !Array.isArray(data)) {
        return;
    }

    const key = resolveKey(canvas);
    if (!key) {
        return;
    }

    const module = await ensureChartJs();
    const { Chart } = module;

    const datasetLabel = options?.DatasetLabel ?? "Series";
    const color = options?.Color ?? "#0d6efd";
    const yAxisLabel = options?.YAxisLabel ?? "";
    const valueSuffix = options?.ValueSuffix ?? "";
    const xAxisLabel = options?.XAxisLabel ?? "Samples";

    const points = data.map(Number);
    const labels = points.map((_, index) => (index + 1).toString());

    let chart = charts.get(key);
    if (!chart) {
        chart = new Chart(canvas, {
            type: "line",
            data: {
                labels,
                datasets: [
                    {
                        label: datasetLabel,
                        data: points,
                        borderColor: color,
                        backgroundColor: color,
                        borderWidth: 1.5,
                        tension: 0.35,
                        pointRadius: 0,
                        pointHoverRadius: 3,
                        fill: false
                    }
                ]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                animation: false,
                scales: {
                    x: {
                        display: true,
                        title: {
                            display: true,
                            text: xAxisLabel,
                            color: "rgba(255, 255, 255, 0.65)",
                            font: {
                                size: 11,
                                weight: "600"
                            }
                        },
                        ticks: {
                            color: "rgba(255, 255, 255, 0.55)",
                            maxTicksLimit: 6
                        },
                        grid: {
                            color: "rgba(255, 255, 255, 0.08)"
                        }
                    },
                    y: {
                        beginAtZero: true,
                        display: true,
                        title: {
                            display: !!yAxisLabel,
                            text: yAxisLabel,
                            color: "rgba(255, 255, 255, 0.65)",
                            font: {
                                size: 11,
                                weight: "600"
                            }
                        },
                        ticks: {
                            color: "rgba(255, 255, 255, 0.55)",
                            maxTicksLimit: 5,
                            callback(value) {
                                return formatValue(Number(value), valueSuffix);
                            }
                        },
                        grid: {
                            color: "rgba(255, 255, 255, 0.08)"
                        }
                    }
                },
                plugins: {
                    tooltip: {
                        callbacks: {
                            label(context) {
                                const value = context.parsed.y ?? 0;
                                return `${datasetLabel}: ${formatValue(value, valueSuffix)}`;
                            }
                        }
                    }
                }
            }
        });
        charts.set(key, chart);
    } else {
        chart.data.labels = labels;
        chart.data.datasets[0].data = points;
        chart.update("none");
    }
}

export function destroyChart(target) {
    const key = resolveKey(target);
    if (!key) {
        return;
    }

    const chart = charts.get(key);
    if (chart) {
        chart.destroy();
        charts.delete(key);
    }
}

export function dispose() {
    charts.forEach(chart => {
        try {
            chart.destroy();
        } catch {
            // ignored
        }
    });
    charts.clear();
}
