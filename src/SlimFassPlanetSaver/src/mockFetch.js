// Initial state for /status-functions
let currentStatusFunctionsBody = [
    { NumberReady: 0, numberRequested: 0, PodType: "Deployment", Visibility: "Public", Name: "fibonacci1" },
    { NumberReady: 0, numberRequested: 0, PodType: "Deployment", Visibility: "Public", Name: "fibonacci2" },
    { NumberReady: 0, numberRequested: 1, PodType: "Deployment", Visibility: "Public", Name: "fibonacci3" },
    { NumberReady: 0, numberRequested: 2, PodType: "Deployment", Visibility: "Private", Name: "fibonacci4" }
];

// Alternate body for /status-functions
const alternateStatusFunctionsBody = [
    { NumberReady: 1, numberRequested: 1, PodType: "Deployment", Visibility: "Public", Name: "fibonacci1" },
    { NumberReady: 1, numberRequested: 1, PodType: "Deployment", Visibility: "Public", Name: "fibonacci2" },
    { NumberReady: 1, numberRequested: 1, PodType: "Deployment", Visibility: "Public", Name: "fibonacci3" },
    { NumberReady: 2, numberRequested: 2, PodType: "Deployment", Visibility: "Private", Name: "fibonacci4" }
];

// Function to toggle between the two bodies every 15 seconds
setInterval(() => {
    currentStatusFunctionsBody =
        currentStatusFunctionsBody === alternateStatusFunctionsBody
            ? [
                { NumberReady: 0, numberRequested: 0, PodType: "Deployment", Visibility: "Public", Name: "fibonacci1" },
                { NumberReady: 0, numberRequested: 0, PodType: "Deployment", Visibility: "Public", Name: "fibonacci2" },
                { NumberReady: 0, numberRequested: 1, PodType: "Deployment", Visibility: "Public", Name: "fibonacci3" },
                { NumberReady: 0, numberRequested: 2, PodType: "Deployment", Visibility: "Private", Name: "fibonacci4" }
            ]
            : alternateStatusFunctionsBody;
}, 8000); // 15 seconds

// Mock fetch function
function mockFetch(url, options = {}) {
    return new Promise((resolve, reject) => {
        // Route: /status-functions
        if (url === "https://slimfaas/status-functions" && (!options.method || options.method === "GET")) {
            resolve({
                status: 200,
                ok: true,
                json: () => Promise.resolve(currentStatusFunctionsBody),
                text: () => Promise.resolve(JSON.stringify(currentStatusFunctionsBody)),
            });
        }
        // Routes: /wake-function/fibonacci1, /wake-function/fibonacci2, /wake-function/fibonacci3, /wake-function/fibonacci4
        else if (
            url.startsWith("https://slimfaas/wake-function") &&
            options.method === "POST"
        ) {
            resolve({
                status: 204,
                ok: true,
                text: () => Promise.resolve(""),
                json: () => Promise.resolve({}),
            });
        } else {
            // Fallback for unhandled routes
            resolve({
                status: 404,
                ok: false,
                text: () => Promise.resolve("Not Found"),
                json: () => Promise.reject(new Error("Not Found")),
            });
        }
    });
}

export default mockFetch;
