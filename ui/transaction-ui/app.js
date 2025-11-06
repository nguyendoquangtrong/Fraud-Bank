const TRANSACTION_API_BASE = "http://localhost:5032";

const form = document.getElementById("transferForm");
const feedback = document.getElementById("feedback");
const submitBtn = document.getElementById("submitBtn");
const resetBtn = document.getElementById("resetBtn");

const accountCache = new Map();
const currencyFormatter = new Intl.NumberFormat("vi-VN", {
    style: "currency",
    currency: "VND",
    maximumFractionDigits: 0
});

const resolveTransactionApi = (path) => {
    const base = TRANSACTION_API_BASE.endsWith("/")
        ? TRANSACTION_API_BASE
        : `${TRANSACTION_API_BASE}/`;
    return new URL(path.replace(/^\/+/, ""), base).toString();
};

const setFeedback = (kind, message) => {
    feedback.classList.remove("hidden", "error", "pending");
    feedback.classList.toggle("error", kind === "error");
    feedback.classList.toggle("pending", kind === "pending");
    feedback.textContent = message;
};

const clearFeedback = () => {
    feedback.classList.add("hidden");
    feedback.classList.remove("error", "pending");
    feedback.textContent = "";
};

const setupAccountPreview = (fieldName, containerId) => {
    const input = form.elements.namedItem(fieldName);
    const container = document.getElementById(containerId);
    const holder = container.querySelector("[data-role='holder']");
    const meta = container.querySelector("[data-role='meta']");
    const state = { timer: null, requestId: 0 };

    const setDisplay = (status, holderText, metaText = "") => {
        container.dataset.state = status;
        holder.textContent = holderText;
        meta.textContent = metaText;
        meta.style.display = metaText ? "block" : "none";
    };

    const fetchAccount = async (accountNo) => {
        const trimmed = accountNo.trim();
        if (!trimmed) {
            return { status: "idle" };
        }

        const cacheKey = `${TRANSACTION_API_BASE}|${trimmed}`;
        if (!accountCache.has(cacheKey)) {
            const promise = (async () => {
                try {
                    const endpoint = resolveTransactionApi(`/api/accounts/${encodeURIComponent(trimmed)}`);
                    const res = await fetch(endpoint);
                    if (res.status === 404) {
                        return { status: "not-found" };
                    }
                    if (!res.ok) {
                        return {
                            status: "error",
                            error: `Server returned ${res.status}`
                        };
                    }
                    const data = await res.json();
                    return { status: "found", data };
                } catch (error) {
                    return {
                        status: "error",
                        error: error instanceof Error ? error.message : String(error)
                    };
                }
            })();
            accountCache.set(cacheKey, promise);
        }

        return accountCache.get(cacheKey);
    };

    const runLookup = async (accountNo) => {
        const currentRequest = ++state.requestId;
        const trimmed = accountNo.trim();

        if (!trimmed) {
            setDisplay("idle", "Nhập số tài khoản để kiểm tra");
            return;
        }

        setDisplay("loading", "Đang kiểm tra...");
        const result = await fetchAccount(trimmed);

        if (currentRequest !== state.requestId) {
            return;
        }

        switch (result.status) {
            case "idle":
                setDisplay("idle", "Nhập số tài khoản để kiểm tra");
                break;
            case "found": {
                const { data } = result;
                const holderName = data?.holderName?.trim() || "Chưa có tên chủ tài khoản";
                const balance = typeof data?.balance === "number" ? data.balance : Number(data?.balance ?? 0);
                const balanceText = Number.isFinite(balance)
                    ? `Số dư hiện tại: ${currencyFormatter.format(balance)}`
                    : "";
                setDisplay("found", holderName, balanceText);
                break;
            }
            case "not-found":
                setDisplay("not-found", "Không tìm thấy tài khoản", "Vui lòng kiểm tra lại.");
                break;
            case "error":
                setDisplay("error", "Không thể kiểm tra tài khoản", result.error ?? "");
                break;
            default:
                setDisplay("error", "Không thể kiểm tra tài khoản", "Trạng thái không xác định.");
        }
    };

    const schedule = (immediate = false) => {
        if (state.timer) {
            clearTimeout(state.timer);
        }
        if (immediate) {
            runLookup(input.value);
        } else {
            state.timer = setTimeout(() => runLookup(input.value), 400);
        }
    };

    input.addEventListener("input", () => schedule(false));
    input.addEventListener("blur", () => schedule(true));

    return {
        lookup: schedule,
        reset: () => setDisplay("idle", "Nhập số tài khoản để kiểm tra")
    };
};

const accountPreviews = [
    setupAccountPreview("fromAccount", "fromAccountPreview"),
    setupAccountPreview("toAccount", "toAccountPreview")
];

accountPreviews.forEach((preview) => preview.lookup(true));

const redirectToResult = (params) => {
    const search = params.toString();
    window.location.href = `result.html${search ? `?${search}` : ""}`;
};

form.addEventListener("submit", async (event) => {
    event.preventDefault();

    const data = Object.fromEntries(new FormData(form).entries());
    const payload = {
        fromAccount: data.fromAccount?.trim() ?? "",
        toAccount: data.toAccount?.trim() ?? "",
        amount: data.amount ? Number(data.amount) : 0
    };

    if (!payload.fromAccount || !payload.toAccount || !payload.amount) {
        setFeedback("error", "Vui lòng nhập đầy đủ thông tin hợp lệ.");
        return;
    }

    let endpoint;
    try {
        endpoint = resolveTransactionApi("/api/transactions/transfer");
    } catch (err) {
        setFeedback("error", err instanceof Error ? err.message : String(err));
        return;
    }

    submitBtn.disabled = true;
    resetBtn.disabled = true;
    setFeedback("pending", "Đang gửi giao dịch...");

    try {
        const res = await fetch(endpoint, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload)
        });

        const text = await res.text();
        let json;
        try {
            json = text ? JSON.parse(text) : {};
        } catch {
            json = { raw: text };
        }

        if (res.ok && res.status === 202) {
            const txId = json.transactionId ?? json.id ?? json.txId ?? null;
            const params = new URLSearchParams({
                status: "DECIDED_REVIEW",
                from: payload.fromAccount,
                to: payload.toAccount,
                amount: payload.amount.toString(),
                message: "Giao dịch đã được ghi nhận và đang chờ hệ thống đánh giá."
            });
            if (txId) {
                params.set("txId", txId);
            }
            redirectToResult(params);
            return;
        }

        const params = new URLSearchParams({
            status: "DECIDED_BLOCK",
            from: payload.fromAccount,
            to: payload.toAccount,
            amount: payload.amount.toString()
        });
        if (json.transactionId) {
            params.set("txId", json.transactionId);
        }
        if (json.error) {
            params.set("message", json.error);
        } else if (json.message) {
            params.set("message", json.message);
        } else {
            params.set("message", `Unexpected response (${res.status})`);
        }
        redirectToResult(params);
    } catch (error) {
        const params = new URLSearchParams({
            status: "DECIDED_BLOCK",
            from: payload.fromAccount,
            to: payload.toAccount,
            amount: payload.amount.toString(),
            message: error instanceof Error ? error.message : String(error)
        });
        redirectToResult(params);
    } finally {
        submitBtn.disabled = false;
        resetBtn.disabled = false;
    }
});

form.addEventListener("reset", () => {
    clearFeedback();
    accountPreviews.forEach((preview) => preview.reset());
});
