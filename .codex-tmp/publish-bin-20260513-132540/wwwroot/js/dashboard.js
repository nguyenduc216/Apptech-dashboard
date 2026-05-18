(function () {
    if ("serviceWorker" in navigator) {
        window.addEventListener("load", () => {
            navigator.serviceWorker.register("/service-worker.js").catch(() => {
            });
        });
    }

    const topbar = document.querySelector(".topbar");
    const searchToggle = document.querySelector("[data-search-toggle]");
    const globalSearch = document.getElementById("globalSearch");
    const accountTrigger = document.querySelector("[data-account-trigger]");
    const accountPopupShell = document.querySelector("[data-account-popup-shell]");
    const accountPopup = document.getElementById("accountMenuPopup");
    const menuTrigger = document.querySelector("[data-menu-trigger]");
    const menuPopupShell = document.querySelector("[data-menu-popup]");
    const menuPopup = menuPopupShell?.querySelector(".menu-popup");
    const menuViewport = menuPopupShell?.querySelector("[data-menu-viewport]");
    const menuTitle = menuPopupShell?.querySelector("[data-menu-title]");
    const menuBack = menuPopupShell?.querySelector("[data-menu-back]");
    const menuDataNode = document.getElementById("menu-data");
    const currentPath = document.body.dataset.currentPath || window.location.pathname;
    const crudModalShell = document.querySelector("[data-crud-modal-shell]");
    const dataActions = document.querySelector("[data-data-actions]");
    const scrollRestoreKey = `apptech:scroll:${window.location.pathname}${window.location.search}`;

    const saveScrollPosition = () => {
        try {
            sessionStorage.setItem(scrollRestoreKey, `${window.scrollY}`);
        } catch {
        }
    };

    const restoreScrollPosition = () => {
        try {
            const storedValue = sessionStorage.getItem(scrollRestoreKey);
            if (!storedValue) {
                return;
            }

            const hasValidationErrors = Boolean(document.querySelector(".login-field.has-error, .validation-summary, .login-validation"));
            sessionStorage.removeItem(scrollRestoreKey);
            if (hasValidationErrors) {
                return;
            }

            const scrollY = Number(storedValue);
            if (Number.isNaN(scrollY) || scrollY <= 0) {
                return;
            }

            requestAnimationFrame(() => {
                window.scrollTo({
                    top: scrollY,
                    left: 0,
                    behavior: "auto"
                });
            });
        } catch {
        }
    };

    window.addEventListener("pagehide", () => {
        saveScrollPosition();
    });

    if (accountTrigger && accountPopupShell && accountPopup) {
        const closeAccountMenu = () => {
            accountPopupShell.hidden = true;
            accountTrigger.setAttribute("aria-expanded", "false");
        };

        const openAccountMenu = () => {
            accountPopupShell.hidden = false;
            accountTrigger.setAttribute("aria-expanded", "true");
            accountPopup.style.left = "50%";
            accountPopup.style.top = "50%";
            accountPopup.style.transform = "translate(-50%, -50%)";
        };

        accountTrigger.addEventListener("click", () => {
            if (accountPopupShell.hidden) {
                openAccountMenu();
                return;
            }

            closeAccountMenu();
        });

        accountPopupShell.querySelectorAll("[data-account-close]").forEach((element) => {
            element.addEventListener("click", () => {
                closeAccountMenu();
            });
        });

        document.addEventListener("keydown", (event) => {
            if (event.key === "Escape" && !accountPopupShell.hidden) {
                closeAccountMenu();
            }
        });
    }

    if (topbar && searchToggle && globalSearch) {
        const mobileQuery = window.matchMedia("(max-width: 768px), (max-height: 540px) and (orientation: landscape)");
        const syncSearchState = () => {
            if (!mobileQuery.matches) {
                topbar.classList.remove("search-open");
                searchToggle.setAttribute("aria-expanded", "false");
            }
        };

        searchToggle.addEventListener("click", () => {
            if (!mobileQuery.matches) {
                globalSearch.focus();
                return;
            }

            const isOpen = topbar.classList.toggle("search-open");
            searchToggle.setAttribute("aria-expanded", String(isOpen));

            if (isOpen) {
                requestAnimationFrame(() => globalSearch.focus());
            }
        });

        document.addEventListener("keydown", (event) => {
            if (event.key === "Escape" && topbar.classList.contains("search-open")) {
                topbar.classList.remove("search-open");
                searchToggle.setAttribute("aria-expanded", "false");
            }
        });

        if (typeof mobileQuery.addEventListener === "function") {
            mobileQuery.addEventListener("change", syncSearchState);
        } else if (typeof mobileQuery.addListener === "function") {
            mobileQuery.addListener(syncSearchState);
        }
    }

    document.querySelectorAll("form").forEach((form) => {
        form.addEventListener("submit", (event) => {
            if (event.defaultPrevented) {
                return;
            }

            const submitter = event.submitter instanceof HTMLButtonElement
                ? event.submitter
                : form.querySelector("[data-processing-button]");

            if (!(submitter instanceof HTMLButtonElement) || !submitter.matches("[data-processing-button]")) {
                return;
            }

            if (submitter.classList.contains("is-processing")) {
                event.preventDefault();
                return;
            }

            const processingText = submitter.dataset.processingText?.trim();
            const processingTextNode = submitter.querySelector(".button-processing-text");
            if (processingText && processingTextNode) {
                processingTextNode.textContent = processingText;
            }

            const submitterName = submitter.getAttribute("name");
            if (submitterName) {
                const submitterValueInput = document.createElement("input");
                submitterValueInput.type = "hidden";
                submitterValueInput.name = submitterName;
                submitterValueInput.value = submitter.value || "";
                form.appendChild(submitterValueInput);
            }

            submitter.classList.add("is-processing");
            submitter.setAttribute("aria-busy", "true");
        }, { capture: true });
    });

    window.addEventListener("pageshow", () => {
        document.querySelectorAll("[data-processing-button].is-processing").forEach((button) => {
            button.classList.remove("is-processing");
            button.disabled = false;
            button.removeAttribute("aria-busy");
        });

        restoreScrollPosition();
    });

    const liveSelectStates = new WeakMap();
    let liveSelectCounter = 0;
    let activeLiveSelect = null;

    const normalizeLiveSelectTerm = (value) => `${value || ""}`
        .normalize("NFD")
        .replace(/[\u0300-\u036f]/g, "")
        .replace(/\s+/g, " ")
        .trim()
        .toLowerCase();

    const syncLiveSelectState = (select) => {
        if (!(select instanceof HTMLSelectElement)) {
            return;
        }

        liveSelectStates.get(select)?.sync();
    };

    const initLiveSelects = (root = document) => {
        const selects = [];
        if (root instanceof HTMLSelectElement) {
            selects.push(root);
        } else if (root instanceof Element || root instanceof Document || root instanceof DocumentFragment) {
            selects.push(...root.querySelectorAll(".select-shell select:not([multiple]):not([data-live-select='false'])"));
        }

        selects.forEach((select) => {
            if (!(select instanceof HTMLSelectElement) ||
                select.multiple ||
                liveSelectStates.has(select)) {
                return;
            }

            const shell = select.closest(".select-shell");
            if (!(shell instanceof HTMLElement) || shell.querySelector(".live-select-trigger")) {
                return;
            }

            const indicator = shell.querySelector(".select-indicator");
            const trigger = document.createElement("button");
            trigger.type = "button";
            trigger.className = "live-select-trigger";
            trigger.setAttribute("aria-haspopup", "listbox");
            trigger.setAttribute("aria-expanded", "false");

            const triggerLabel = document.createElement("span");
            triggerLabel.className = "live-select-trigger-label";
            trigger.appendChild(triggerLabel);

            const panel = document.createElement("div");
            panel.className = "live-select-panel";
            panel.hidden = true;

            const panelId = `liveSelectPanel${++liveSelectCounter}`;
            panel.id = panelId;
            trigger.setAttribute("aria-controls", panelId);

            const searchShell = document.createElement("div");
            searchShell.className = "live-select-search";

            const searchIcon = document.createElement("i");
            searchIcon.className = "fa-solid fa-magnifying-glass";
            searchIcon.setAttribute("aria-hidden", "true");
            searchShell.appendChild(searchIcon);

            const searchInput = document.createElement("input");
            searchInput.type = "search";
            searchInput.className = "live-select-search-input";
            searchInput.placeholder = "Tìm để chọn...";
            searchInput.setAttribute("aria-label", "Tìm trong danh sách");
            searchShell.appendChild(searchInput);

            const optionsNode = document.createElement("div");
            optionsNode.className = "live-select-options";
            optionsNode.setAttribute("role", "listbox");

            const emptyNode = document.createElement("div");
            emptyNode.className = "live-select-empty";
            emptyNode.hidden = true;
            emptyNode.textContent = "Không tìm thấy lựa chọn phù hợp.";

            panel.append(searchShell, optionsNode, emptyNode);

            shell.classList.add("live-select-shell");
            select.classList.add("live-select-native");
            shell.insertBefore(trigger, indicator || null);
            document.body.appendChild(panel);

            let optionButtons = [];
            let activeIndex = -1;

            const setActiveIndex = (index) => {
                activeIndex = index;
                optionButtons.forEach((button, buttonIndex) => {
                    const isActive = buttonIndex === activeIndex;
                    button.classList.toggle("is-active", isActive);
                    button.setAttribute("aria-selected", String(isActive));
                    if (isActive) {
                        button.scrollIntoView({ block: "nearest" });
                    }
                });
            };

            const updatePlacement = () => {
                if (panel.hidden) {
                    return;
                }

                const shellRect = shell.getBoundingClientRect();
                const viewportPadding = 8;
                const panelGap = 10;
                const spaceBelow = window.innerHeight - shellRect.bottom - viewportPadding - panelGap;
                const spaceAbove = shellRect.top - viewportPadding - panelGap;
                const openUpward = spaceBelow < 260 && spaceAbove > spaceBelow;
                const availableSpace = Math.max(160, openUpward ? spaceAbove : spaceBelow);
                const optionsMaxHeight = Math.max(96, Math.min(240, availableSpace - 86));
                const estimatedHeight = Math.min(panel.scrollHeight, availableSpace);
                const top = openUpward
                    ? Math.max(viewportPadding, shellRect.top - estimatedHeight - panelGap)
                    : Math.min(window.innerHeight - estimatedHeight - viewportPadding, shellRect.bottom + panelGap);

                optionsNode.style.maxHeight = `${optionsMaxHeight}px`;
                panel.style.width = `${shellRect.width}px`;
                panel.style.left = `${Math.max(viewportPadding, Math.min(shellRect.left, window.innerWidth - shellRect.width - viewportPadding))}px`;
                panel.style.top = `${Math.max(viewportPadding, top)}px`;
            };

            const renderOptions = () => {
                const normalizedQuery = normalizeLiveSelectTerm(searchInput.value);
                const items = Array.from(select.options)
                    .map((option, index) => ({
                        option,
                        index,
                        text: option.textContent?.trim() || "",
                        value: option.value || "",
                        searchText: normalizeLiveSelectTerm(`${option.textContent || ""} ${option.value || ""}`)
                    }))
                    .filter((item) => !normalizedQuery || item.searchText.includes(normalizedQuery));

                optionsNode.innerHTML = "";
                optionButtons = [];
                activeIndex = -1;

                if (items.length === 0) {
                    emptyNode.hidden = false;
                    updatePlacement();
                    return;
                }

                emptyNode.hidden = true;
                const fragment = document.createDocumentFragment();

                items.forEach((item, index) => {
                    const button = document.createElement("button");
                    button.type = "button";
                    button.className = "live-select-option";
                    button.setAttribute("role", "option");
                    button.dataset.optionIndex = `${item.index}`;
                    button.textContent = item.text;

                    if (item.option.disabled) {
                        button.disabled = true;
                    }

                    if (item.option.selected) {
                        button.classList.add("is-selected");
                        activeIndex = index;
                    }

                    button.addEventListener("mouseenter", () => {
                        if (!button.disabled) {
                            setActiveIndex(index);
                        }
                    });

                    button.addEventListener("click", () => {
                        if (button.disabled) {
                            return;
                        }

                        const previousValue = select.value;
                        select.value = item.option.value;
                        syncLiveSelectState(select);
                        closeLiveSelect();
                        trigger.focus({ preventScroll: true });

                        if (previousValue !== select.value) {
                            select.dispatchEvent(new Event("change", { bubbles: true }));
                        }
                    });

                    optionButtons.push(button);
                    fragment.appendChild(button);
                });

                optionsNode.appendChild(fragment);
                setActiveIndex(activeIndex >= 0 ? activeIndex : optionButtons.findIndex((button) => !button.disabled));
                updatePlacement();
            };

            const openLiveSelect = () => {
                if (select.disabled) {
                    return;
                }

                if (activeLiveSelect && activeLiveSelect !== state) {
                    activeLiveSelect.close();
                }

                activeLiveSelect = state;
                panel.hidden = false;
                shell.classList.add("is-open");
                trigger.setAttribute("aria-expanded", "true");
                searchInput.value = "";
                renderOptions();
                requestAnimationFrame(() => {
                    searchInput.focus({ preventScroll: true });
                    searchInput.select();
                });
            };

            const closeLiveSelect = () => {
                if (panel.hidden) {
                    return;
                }

                panel.hidden = true;
                shell.classList.remove("is-open");
                trigger.setAttribute("aria-expanded", "false");
                searchInput.value = "";
                optionButtons = [];
                activeIndex = -1;

                if (activeLiveSelect === state) {
                    activeLiveSelect = null;
                }
            };

            const sync = () => {
                const selectedOption = select.selectedOptions[0] || select.options[0] || null;
                const selectedText = selectedOption?.textContent?.trim() || "Chọn giá trị";
                const isPlaceholder = !select.value;

                triggerLabel.textContent = selectedText;
                triggerLabel.title = selectedText;
                trigger.classList.toggle("is-placeholder", isPlaceholder);
                trigger.disabled = select.disabled;
                shell.classList.toggle("is-disabled", select.disabled);

                if (!panel.hidden) {
                    renderOptions();
                }
            };

            const state = {
                containsTarget: (target) => target instanceof Node && (shell.contains(target) || panel.contains(target)),
                close: closeLiveSelect,
                open: openLiveSelect,
                reposition: updatePlacement,
                sync
            };

            trigger.addEventListener("click", () => {
                if (panel.hidden) {
                    openLiveSelect();
                    return;
                }

                closeLiveSelect();
            });

            trigger.addEventListener("keydown", (event) => {
                if (!["Enter", " ", "ArrowDown", "ArrowUp"].includes(event.key)) {
                    return;
                }

                event.preventDefault();
                openLiveSelect();
            });

            searchInput.addEventListener("input", () => {
                renderOptions();
            });

            searchInput.addEventListener("keydown", (event) => {
                if (event.key === "Escape") {
                    event.preventDefault();
                    closeLiveSelect();
                    trigger.focus({ preventScroll: true });
                    return;
                }

                if (event.key === "ArrowDown") {
                    event.preventDefault();
                    if (optionButtons.length === 0) {
                        return;
                    }

                    let nextIndex = activeIndex;
                    do {
                        nextIndex = (nextIndex + 1) % optionButtons.length;
                    } while (optionButtons[nextIndex]?.disabled && nextIndex !== activeIndex);
                    setActiveIndex(nextIndex);
                    return;
                }

                if (event.key === "ArrowUp") {
                    event.preventDefault();
                    if (optionButtons.length === 0) {
                        return;
                    }

                    let nextIndex = activeIndex < 0 ? optionButtons.length : activeIndex;
                    do {
                        nextIndex = (nextIndex - 1 + optionButtons.length) % optionButtons.length;
                    } while (optionButtons[nextIndex]?.disabled && nextIndex !== activeIndex);
                    setActiveIndex(nextIndex);
                    return;
                }

                if (event.key === "Enter" && activeIndex >= 0) {
                    event.preventDefault();
                    optionButtons[activeIndex]?.click();
                }
            });

            searchInput.addEventListener("blur", () => {
                window.setTimeout(() => {
                    const activeElement = document.activeElement;
                    if (activeElement instanceof Node && state.containsTarget(activeElement)) {
                        return;
                    }

                    closeLiveSelect();
                }, 120);
            });

            select.addEventListener("change", sync);
            select.addEventListener("input", sync);

            const optionsObserver = new MutationObserver(() => {
                sync();
            });

            optionsObserver.observe(select, {
                childList: true,
                subtree: true,
                attributes: true,
                attributeFilter: ["disabled", "label", "selected", "value"]
            });

            liveSelectStates.set(select, state);
            sync();
        });
    };

    document.addEventListener("pointerdown", (event) => {
        if (!activeLiveSelect) {
            return;
        }

        if (activeLiveSelect.containsTarget?.(event.target)) {
            return;
        }

        activeLiveSelect.close();
    });

    window.addEventListener("resize", () => {
        activeLiveSelect?.reposition?.();
    });

    window.addEventListener("scroll", () => {
        activeLiveSelect?.reposition?.();
    }, true);

    initLiveSelects(document);

    if (document.body) {
        const liveSelectObserver = new MutationObserver((mutations) => {
            mutations.forEach((mutation) => {
                if (mutation.target instanceof HTMLSelectElement) {
                    syncLiveSelectState(mutation.target);
                }

                mutation.addedNodes.forEach((node) => {
                    if (node instanceof HTMLOptionElement && node.parentElement instanceof HTMLSelectElement) {
                        syncLiveSelectState(node.parentElement);
                        return;
                    }

                    if (node instanceof Element || node instanceof DocumentFragment) {
                        initLiveSelects(node);
                    }
                });
            });
        });

        liveSelectObserver.observe(document.body, {
            childList: true,
            subtree: true
        });
    }

    window.ApptechLiveSelect = {
        init: initLiveSelects,
        refresh: syncLiveSelectState
    };

    document.querySelectorAll("[data-password-toggle]").forEach((button) => {
        const inputId = button.getAttribute("aria-controls");
        const input = inputId
            ? document.getElementById(inputId)
            : button.closest(".login-input-shell")?.querySelector("input");
        const icon = button.querySelector("i");

        if (!(input instanceof HTMLInputElement) || !icon) {
            return;
        }

        button.addEventListener("click", () => {
            const isVisible = input.type === "text";
            input.type = isVisible ? "password" : "text";
            button.setAttribute("aria-pressed", String(!isVisible));
            button.setAttribute("aria-label", isVisible ? "Hiển thị mật khẩu" : "Ẩn mật khẩu");
            icon.classList.toggle("fa-eye", !isVisible);
            icon.classList.toggle("fa-eye-slash", isVisible);
            input.focus({ preventScroll: true });
            input.setSelectionRange(input.value.length, input.value.length);
        });
    });

    document.querySelectorAll("[data-digit-group-input]").forEach((input) => {
        if (!(input instanceof HTMLInputElement)) {
            return;
        }

        const shell = input.closest(".login-input-shell");
        const hiddenInput = shell?.querySelector("[data-digit-group-target]");
        if (!(hiddenInput instanceof HTMLInputElement)) {
            return;
        }

        const formatter = new Intl.NumberFormat(input.dataset.digitGroupLocale || "vi-VN", {
            maximumFractionDigits: 0
        });

        const syncValue = () => {
            const digits = `${input.value || ""}`.replace(/\D/g, "");
            hiddenInput.value = digits;
            input.value = digits ? formatter.format(Number(digits)) : "";
        };

        input.addEventListener("keydown", (event) => {
            if (event.ctrlKey || event.metaKey || event.altKey) {
                return;
            }

            const allowedKeys = new Set([
                "Backspace",
                "Delete",
                "Tab",
                "Escape",
                "Enter",
                "ArrowLeft",
                "ArrowRight",
                "ArrowUp",
                "ArrowDown",
                "Home",
                "End"
            ]);

            if (allowedKeys.has(event.key)) {
                return;
            }

            if (!/^\d$/.test(event.key)) {
                event.preventDefault();
            }
        });

        input.addEventListener("input", () => {
            syncValue();
        });

        input.addEventListener("paste", () => {
            window.setTimeout(syncValue, 0);
        });

        if (input.value) {
            syncValue();
            return;
        }

        const initialDigits = `${hiddenInput.value || ""}`.replace(/\D/g, "");
        if (initialDigits) {
            input.value = formatter.format(Number(initialDigits));
        }
    });

    const avatarInput = document.querySelector("[data-avatar-input]");
    const avatarPreview = document.querySelector("[data-avatar-preview]");
    const avatarFallback = document.querySelector("[data-avatar-fallback]");
    if (avatarInput instanceof HTMLInputElement && avatarPreview instanceof HTMLImageElement && avatarFallback) {
        avatarInput.addEventListener("change", () => {
            const [file] = avatarInput.files || [];
            if (!file) {
                avatarPreview.removeAttribute("src");
                avatarPreview.classList.add("is-hidden");
                avatarFallback.classList.remove("is-hidden");
                return;
            }

            avatarPreview.src = URL.createObjectURL(file);
            avatarPreview.classList.remove("is-hidden");
            avatarFallback.classList.add("is-hidden");
        });
    }

    document.querySelectorAll("[data-image-upload]").forEach((editor) => {
        const input = editor.querySelector("[data-image-input]");
        const preview = editor.querySelector("[data-image-preview]");
        const fallback = editor.querySelector("[data-image-fallback]");

        if (!(input instanceof HTMLInputElement) || !(preview instanceof HTMLImageElement) || !fallback) {
            return;
        }

        input.addEventListener("change", () => {
            const [file] = input.files || [];
            if (!file) {
                if (preview.getAttribute("src")) {
                    preview.classList.remove("is-hidden");
                    fallback.classList.add("is-hidden");
                    return;
                }

                preview.removeAttribute("src");
                preview.classList.add("is-hidden");
                fallback.classList.remove("is-hidden");
                return;
            }

            preview.src = URL.createObjectURL(file);
            preview.classList.remove("is-hidden");
            fallback.classList.add("is-hidden");
        });
    });

    const vatTuDraftStore = (() => {
        const databaseName = "apptech-dashboard";
        const storeName = "vat-tu-image-drafts";

        const openDatabase = () => new Promise((resolve, reject) => {
            if (!("indexedDB" in window)) {
                reject(new Error("IndexedDB is unavailable."));
                return;
            }

            const request = window.indexedDB.open(databaseName, 1);
            request.onupgradeneeded = () => {
                const database = request.result;
                if (!database.objectStoreNames.contains(storeName)) {
                    database.createObjectStore(storeName);
                }
            };
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error || new Error("Failed to open IndexedDB."));
        });

        const withStore = async (mode, work) => {
            const database = await openDatabase();
            return new Promise((resolve, reject) => {
                const transaction = database.transaction(storeName, mode);
                const store = transaction.objectStore(storeName);
                const request = work(store);

                transaction.oncomplete = () => {
                    database.close();
                };
                transaction.onerror = () => {
                    database.close();
                    reject(transaction.error || new Error("IndexedDB transaction failed."));
                };

                if (request) {
                    request.onsuccess = () => resolve(request.result);
                    request.onerror = () => reject(request.error || new Error("IndexedDB request failed."));
                    return;
                }

                resolve(null);
            });
        };

        const get = async (key) => withStore("readonly", (store) => store.get(key));
        const set = async (key, value) => withStore("readwrite", (store) => store.put(value, key));
        const remove = async (key) => withStore("readwrite", (store) => store.delete(key));

        return { get, set, remove };
    })();

    document.querySelectorAll("[data-image-manager]").forEach((manager) => {
        const gallery = manager.querySelector("[data-image-gallery]");
        const emptyState = manager.querySelector("[data-image-empty]");
        const primaryPreview = manager.querySelector("[data-image-primary-preview]");
        const primaryEmpty = manager.querySelector("[data-image-primary-empty]");
        const masterInput = manager.querySelector("[data-image-master]");
        const sourceInputs = Array.from(manager.querySelectorAll("[data-image-source]"));
        const removedInputsHost = manager.closest("form")?.querySelector("[data-image-removed-inputs]");

        if (!(gallery instanceof HTMLElement) ||
            !(emptyState instanceof HTMLElement) ||
            !(primaryPreview instanceof HTMLImageElement) ||
            !(primaryEmpty instanceof HTMLElement) ||
            !(masterInput instanceof HTMLInputElement) ||
            !(removedInputsHost instanceof HTMLElement)) {
            return;
        }

        let pendingFiles = [];
        let removedExistingUrls = Array.from(removedInputsHost.querySelectorAll("input[name='Form.RemovedImageUrls']"))
            .map((input) => input.value)
            .filter((value) => value);

        const buildPendingCard = (pendingItem) => {
            const card = document.createElement("article");
            card.className = "vat-tu-image-card is-pending";
            card.dataset.imageItem = "";
            card.dataset.fileId = pendingItem.id;

            const image = document.createElement("img");
            image.src = pendingItem.objectUrl;
            image.alt = pendingItem.file.name || "Ảnh mới";
            image.loading = "lazy";

            const footer = document.createElement("div");
            footer.className = "vat-tu-image-card-footer";

            const badge = document.createElement("span");
            badge.className = "vat-tu-image-badge";
            badge.textContent = "Ảnh mới";

            const removeButton = document.createElement("button");
            removeButton.type = "button";
            removeButton.className = "vat-tu-image-remove";
            removeButton.dataset.imageRemovePending = "";
            removeButton.setAttribute("aria-label", "Xóa ảnh mới");
            removeButton.innerHTML = '<i class="fa-solid fa-trash-can" aria-hidden="true"></i>';

            footer.append(badge, removeButton);
            card.append(image, footer);
            return card;
        };

        const syncRemovedInputs = () => {
            removedInputsHost.innerHTML = "";
            removedExistingUrls.forEach((imageUrl) => {
                const input = document.createElement("input");
                input.type = "hidden";
                input.name = "Form.RemovedImageUrls";
                input.value = imageUrl;
                removedInputsHost.appendChild(input);
            });
        };

        const syncMasterInput = () => {
            const dataTransfer = new DataTransfer();
            pendingFiles.forEach((pendingItem) => {
                dataTransfer.items.add(pendingItem.file);
            });
            masterInput.files = dataTransfer.files;
        };

        const updatePrimaryPreview = () => {
            const cards = Array.from(gallery.querySelectorAll("[data-image-item]"));
            emptyState.classList.toggle("is-hidden", cards.length > 0);

            cards.forEach((card, index) => {
                card.classList.toggle("is-primary", index === 0);
                const badge = card.querySelector(".vat-tu-image-badge");
                if (!(badge instanceof HTMLElement)) {
                    return;
                }

                if (index === 0) {
                    badge.textContent = "Ảnh đại diện";
                    return;
                }

                badge.textContent = card.hasAttribute("data-image-existing") ? "Đã lưu" : "Ảnh mới";
            });

            const firstImage = cards[0]?.querySelector("img");
            if (firstImage instanceof HTMLImageElement && firstImage.getAttribute("src")) {
                primaryPreview.src = firstImage.getAttribute("src") || "";
                primaryPreview.classList.remove("is-hidden");
                primaryEmpty.classList.add("is-hidden");
                return;
            }

            primaryPreview.removeAttribute("src");
            primaryPreview.classList.add("is-hidden");
            primaryEmpty.classList.remove("is-hidden");
        };

        sourceInputs.forEach((input) => {
            if (!(input instanceof HTMLInputElement)) {
                return;
            }

            input.addEventListener("change", () => {
                const files = Array.from(input.files || []);
                if (files.length === 0) {
                    return;
                }

                files.forEach((file) => {
                    const pendingItem = {
                        id: `${Date.now()}-${Math.random().toString(16).slice(2)}`,
                        file,
                        objectUrl: URL.createObjectURL(file)
                    };

                    pendingFiles.push(pendingItem);
                    gallery.appendChild(buildPendingCard(pendingItem));
                });

                syncMasterInput();
                updatePrimaryPreview();
                void persistDraft();
                input.value = "";
            });
        });

        gallery.addEventListener("click", (event) => {
            const removeExistingButton = event.target.closest("[data-image-remove-existing]");
            if (removeExistingButton instanceof HTMLElement) {
                const card = removeExistingButton.closest("[data-image-existing]");
                const imageUrl = card?.getAttribute("data-image-url");
                if (card instanceof HTMLElement && imageUrl) {
                    if (!removedExistingUrls.includes(imageUrl)) {
                        removedExistingUrls.push(imageUrl);
                        syncRemovedInputs();
                    }

                    card.remove();
                    updatePrimaryPreview();
                }

                return;
            }

            const removePendingButton = event.target.closest("[data-image-remove-pending]");
            if (!(removePendingButton instanceof HTMLElement)) {
                return;
            }

            const card = removePendingButton.closest("[data-file-id]");
            const fileId = card?.getAttribute("data-file-id");
            if (!(card instanceof HTMLElement) || !fileId) {
                return;
            }

            const pendingIndex = pendingFiles.findIndex((pendingItem) => pendingItem.id === fileId);
            if (pendingIndex >= 0) {
                URL.revokeObjectURL(pendingFiles[pendingIndex].objectUrl);
                pendingFiles.splice(pendingIndex, 1);
                syncMasterInput();
            }

            card.remove();
            updatePrimaryPreview();
        });

        window.addEventListener("pagehide", () => {
            pendingFiles.forEach((pendingItem) => {
                URL.revokeObjectURL(pendingItem.objectUrl);
            });
            pendingFiles = [];
        });

        syncRemovedInputs();
        updatePrimaryPreview();
    });

    document.querySelectorAll("[data-image-manager-v2]").forEach((manager) => {
        const form = manager.closest("form");
        const gallery = manager.querySelector("[data-image-gallery]");
        const emptyState = manager.querySelector("[data-image-empty]");
        const primaryPreview = manager.querySelector("[data-image-primary-preview]");
        const primaryEmpty = manager.querySelector("[data-image-primary-empty]");
        const masterInput = manager.querySelector("[data-image-master]");
        const primaryInput = form?.querySelector("[data-image-primary-input]");
        const sourceInputs = Array.from(manager.querySelectorAll("[data-image-source]"));
        const removedInputsHost = form?.querySelector("[data-image-removed-inputs]");
        const viewer = manager.querySelector("[data-image-viewer]");
        const viewerContent = manager.querySelector("[data-image-viewer-content]");
        const viewerCloseButtons = Array.from(manager.querySelectorAll("[data-image-viewer-close]"));
        const zoomInButton = manager.querySelector("[data-image-zoom-in]");
        const zoomOutButton = manager.querySelector("[data-image-zoom-out]");
        const rotateLeftButton = manager.querySelector("[data-image-rotate-left]");
        const rotateRightButton = manager.querySelector("[data-image-rotate-right]");
        const resetViewButton = manager.querySelector("[data-image-reset-view]");
        const cameraOpenButton = manager.querySelector("[data-image-camera-open]");
        const cameraShell = manager.querySelector("[data-image-camera-shell]");
        const cameraCloseButtons = Array.from(manager.querySelectorAll("[data-image-camera-close]"));
        const cameraVideo = manager.querySelector("[data-image-camera-video]");
        const cameraCanvas = manager.querySelector("[data-image-camera-canvas]");
        const cameraPreview = manager.querySelector("[data-image-camera-preview]");
        const cameraCaptureButton = manager.querySelector("[data-image-camera-capture]");
        const cameraRetakeButton = manager.querySelector("[data-image-camera-retake]");
        const cameraSaveButton = manager.querySelector("[data-image-camera-save]");
        const cameraStatus = manager.querySelector("[data-image-camera-status]");

        if (!(gallery instanceof HTMLElement) ||
            !(emptyState instanceof HTMLElement) ||
            !(primaryPreview instanceof HTMLImageElement) ||
            !(primaryEmpty instanceof HTMLElement) ||
            !(masterInput instanceof HTMLInputElement) ||
            !(primaryInput instanceof HTMLInputElement) ||
            !(removedInputsHost instanceof HTMLElement) ||
            !(viewer instanceof HTMLElement) ||
            !(viewerContent instanceof HTMLImageElement) ||
            !(zoomInButton instanceof HTMLButtonElement) ||
            !(zoomOutButton instanceof HTMLButtonElement) ||
            !(rotateLeftButton instanceof HTMLButtonElement) ||
            !(rotateRightButton instanceof HTMLButtonElement) ||
            !(resetViewButton instanceof HTMLButtonElement)) {
            return;
        }

        const formIdInput = form?.querySelector("input[name='Form.Id']");
        const draftKey = `vat-tu:${formIdInput instanceof HTMLInputElement && formIdInput.value ? formIdInput.value : "new"}`;
        const shouldRestoreDraft = Boolean(form?.querySelector(".validation-summary, .login-field.has-error"));

        let pendingFiles = [];
        let removedExistingUrls = Array.from(removedInputsHost.querySelectorAll("input[name='Form.RemovedImageUrls']"))
            .map((input) => input.value)
            .filter((value) => value);
        let selectedPrimary = primaryInput.value.startsWith("existing:")
            ? { type: "existing", key: primaryInput.value.slice("existing:".length) }
            : null;
        let viewerScale = 1;
        let viewerRotation = 0;
        let cameraStream = null;
        let capturedCameraBlob = null;
        let capturedCameraPreviewUrl = "";

        const getCards = () => Array.from(gallery.querySelectorAll("[data-image-item]"));

        const buildPendingCard = (pendingItem) => {
            const card = document.createElement("article");
            card.className = "vat-tu-image-card is-pending";
            card.dataset.imageItem = "";
            card.dataset.fileId = pendingItem.id;
            card.dataset.imageSelection = "";

            const image = document.createElement("img");
            image.src = pendingItem.objectUrl;
            image.alt = pendingItem.file.name || "Anh moi";
            image.loading = "lazy";

            const tools = document.createElement("div");
            tools.className = "vat-tu-image-card-tools";

            const primaryButton = document.createElement("button");
            primaryButton.type = "button";
            primaryButton.className = "vat-tu-image-primary-toggle";
            primaryButton.dataset.imageMakePrimary = "";
            primaryButton.dataset.imageAction = "";
            primaryButton.setAttribute("aria-label", "Chon lam anh dai dien");
            primaryButton.setAttribute("title", "Chon lam anh dai dien");
            primaryButton.innerHTML = '<i class="fa-solid fa-check" aria-hidden="true"></i>';
            tools.appendChild(primaryButton);

            const footer = document.createElement("div");
            footer.className = "vat-tu-image-card-footer";

            const badge = document.createElement("span");
            badge.className = "vat-tu-image-badge";
            badge.textContent = "Anh moi";

            const removeButton = document.createElement("button");
            removeButton.type = "button";
            removeButton.className = "vat-tu-image-remove";
            removeButton.dataset.imageRemovePending = "";
            removeButton.dataset.imageAction = "";
            removeButton.setAttribute("aria-label", "Xoa anh moi");
            removeButton.innerHTML = '<i class="fa-solid fa-trash-can" aria-hidden="true"></i>';

            footer.append(badge, removeButton);
            card.append(image, tools, footer);
            return card;
        };

        const addPendingFile = (file) => {
            const pendingItem = {
                id: `${Date.now()}-${Math.random().toString(16).slice(2)}`,
                file,
                objectUrl: URL.createObjectURL(file),
                selection: ""
            };

            pendingFiles.push(pendingItem);
            gallery.appendChild(buildPendingCard(pendingItem));
            syncMasterInput();
            updatePrimaryPreview();
            void persistDraft();
        };

        const persistDraft = async () => {
            try {
                await vatTuDraftStore.set(draftKey, {
                    pendingFiles: pendingFiles.map((pendingItem) => ({
                        id: pendingItem.id,
                        file: pendingItem.file
                    })),
                    selectedPrimary
                });
            } catch {
            }
        };

        const clearDraft = async () => {
            try {
                await vatTuDraftStore.remove(draftKey);
            } catch {
            }
        };

        const syncRemovedInputs = () => {
            removedInputsHost.innerHTML = "";
            removedExistingUrls.forEach((imageUrl) => {
                const input = document.createElement("input");
                input.type = "hidden";
                input.name = "Form.RemovedImageUrls";
                input.value = imageUrl;
                removedInputsHost.appendChild(input);
            });
        };

        const syncMasterInput = () => {
            const dataTransfer = new DataTransfer();
            pendingFiles.forEach((pendingItem, index) => {
                pendingItem.selection = `new:${index}`;
                const card = gallery.querySelector(`[data-file-id="${pendingItem.id}"]`);
                if (card instanceof HTMLElement) {
                    card.dataset.imageSelection = pendingItem.selection;
                }

                dataTransfer.items.add(pendingItem.file);
            });
            masterInput.files = dataTransfer.files;
        };

        const setSelectedPrimaryFromCard = (card) => {
            if (!(card instanceof HTMLElement)) {
                selectedPrimary = null;
                return;
            }

            if (card.hasAttribute("data-image-existing")) {
                selectedPrimary = {
                    type: "existing",
                    key: card.getAttribute("data-image-url") || ""
                };
                return;
            }

            selectedPrimary = {
                type: "pending",
                key: card.getAttribute("data-file-id") || ""
            };
        };

        const findPrimaryCard = () => {
            const cards = getCards();
            if (cards.length === 0) {
                return null;
            }

            const matchedCard = cards.find((card) => {
                if (!selectedPrimary) {
                    return false;
                }

                if (selectedPrimary.type === "existing") {
                    return card.hasAttribute("data-image-existing") &&
                        card.getAttribute("data-image-url") === selectedPrimary.key;
                }

                return card.getAttribute("data-file-id") === selectedPrimary.key;
            });

            return matchedCard || cards[0] || null;
        };

        const updatePrimaryPreview = () => {
            const cards = getCards();
            emptyState.classList.toggle("is-hidden", cards.length > 0);

            const primaryCard = findPrimaryCard();
            setSelectedPrimaryFromCard(primaryCard);

            cards.forEach((card) => {
                const isPrimary = card === primaryCard;
                const badge = card.querySelector(".vat-tu-image-badge");
                const primaryToggle = card.querySelector("[data-image-make-primary]");

                card.classList.toggle("is-primary", isPrimary);
                if (primaryToggle instanceof HTMLElement) {
                    primaryToggle.classList.toggle("is-active", isPrimary);
                }

                if (badge instanceof HTMLElement) {
                    badge.textContent = isPrimary
                        ? "Anh dai dien"
                        : card.hasAttribute("data-image-existing")
                            ? "Da luu"
                            : "Anh moi";
                }
            });

            primaryInput.value = primaryCard?.getAttribute("data-image-selection") || "";

            const primaryImage = primaryCard?.querySelector("img");
            if (primaryImage instanceof HTMLImageElement && primaryImage.getAttribute("src")) {
                primaryPreview.src = primaryImage.getAttribute("src") || "";
                primaryPreview.classList.remove("is-hidden");
                primaryEmpty.classList.add("is-hidden");
                return;
            }

            primaryPreview.removeAttribute("src");
            primaryPreview.classList.add("is-hidden");
            primaryEmpty.classList.remove("is-hidden");
        };

        const applyViewerTransform = () => {
            viewerContent.style.transform = `scale(${viewerScale}) rotate(${viewerRotation}deg)`;
        };

        const resetViewer = () => {
            viewerScale = 1;
            viewerRotation = 0;
            applyViewerTransform();
        };

        const closeViewer = () => {
            viewer.hidden = true;
            viewerContent.removeAttribute("src");
            resetViewer();
        };

        const openViewer = (imageUrl, altText) => {
            if (!imageUrl) {
                return;
            }

            viewerContent.src = imageUrl;
            viewerContent.alt = altText || "Xem to hinh anh vat tu";
            viewer.hidden = false;
            resetViewer();
        };

        const setCameraStatus = (message, type = "info") => {
            if (!(cameraStatus instanceof HTMLElement)) {
                return;
            }

            cameraStatus.hidden = false;
            cameraStatus.classList.remove("info", "success", "error");
            cameraStatus.classList.add(type);
            cameraStatus.textContent = message;
        };

        const resetCameraCapture = () => {
            capturedCameraBlob = null;
            if (capturedCameraPreviewUrl) {
                URL.revokeObjectURL(capturedCameraPreviewUrl);
                capturedCameraPreviewUrl = "";
            }
            if (cameraPreview instanceof HTMLImageElement) {
                cameraPreview.hidden = true;
                cameraPreview.removeAttribute("src");
            }
            if (cameraVideo instanceof HTMLVideoElement) {
                cameraVideo.hidden = false;
            }
            if (cameraRetakeButton instanceof HTMLButtonElement) {
                cameraRetakeButton.disabled = true;
            }
            if (cameraSaveButton instanceof HTMLButtonElement) {
                cameraSaveButton.disabled = true;
            }
        };

        const stopImageCamera = () => {
            cameraStream?.getTracks().forEach((track) => track.stop());
            cameraStream = null;
            if (cameraVideo instanceof HTMLVideoElement) {
                cameraVideo.srcObject = null;
            }
        };

        const closeImageCamera = () => {
            stopImageCamera();
            resetCameraCapture();
            if (cameraShell instanceof HTMLElement) {
                cameraShell.hidden = true;
            }
        };

        const openImageCamera = async () => {
            if (!(cameraShell instanceof HTMLElement) ||
                !(cameraVideo instanceof HTMLVideoElement) ||
                !navigator.mediaDevices?.getUserMedia) {
                const cameraInput = sourceInputs.find((input) => input.getAttribute("data-image-source") === "camera");
                if (cameraInput instanceof HTMLInputElement) {
                    cameraInput.click();
                }
                return;
            }

            resetCameraCapture();
            cameraShell.hidden = false;
            setCameraStatus("Đang mở camera...", "info");
            try {
                cameraStream = await navigator.mediaDevices.getUserMedia({
                    video: { facingMode: { ideal: "environment" } },
                    audio: false
                });
                cameraVideo.srcObject = cameraStream;
                await cameraVideo.play();
                setCameraStatus("Camera đã sẵn sàng. Vui lòng chụp ảnh vật tư.", "success");
            } catch {
                setCameraStatus("Không thể mở camera. Vui lòng kiểm tra quyền truy cập camera.", "error");
            }
        };

        const captureImageCamera = async () => {
            if (!(cameraVideo instanceof HTMLVideoElement) ||
                !(cameraCanvas instanceof HTMLCanvasElement)) {
                return;
            }

            const width = cameraVideo.videoWidth || 1280;
            const height = cameraVideo.videoHeight || 720;
            cameraCanvas.width = width;
            cameraCanvas.height = height;
            cameraCanvas.getContext("2d")?.drawImage(cameraVideo, 0, 0, width, height);
            capturedCameraBlob = await new Promise((resolve) => cameraCanvas.toBlob(resolve, "image/jpeg", 0.9));
            if (!capturedCameraBlob) {
                setCameraStatus("Không thể chụp ảnh từ camera.", "error");
                return;
            }

            if (capturedCameraPreviewUrl) {
                URL.revokeObjectURL(capturedCameraPreviewUrl);
            }
            capturedCameraPreviewUrl = URL.createObjectURL(capturedCameraBlob);
            if (cameraPreview instanceof HTMLImageElement) {
                cameraPreview.src = capturedCameraPreviewUrl;
                cameraPreview.hidden = false;
            }
            cameraVideo.hidden = true;
            if (cameraRetakeButton instanceof HTMLButtonElement) {
                cameraRetakeButton.disabled = false;
            }
            if (cameraSaveButton instanceof HTMLButtonElement) {
                cameraSaveButton.disabled = false;
            }
            setCameraStatus("Đã chụp ảnh. Bấm Thêm ảnh để đưa vào thư viện.", "success");
        };

        const saveCapturedImage = () => {
            if (!capturedCameraBlob) {
                setCameraStatus("Vui lòng chụp ảnh trước khi thêm.", "error");
                return;
            }

            const file = new File([capturedCameraBlob], `vat-tu-${Date.now()}.jpg`, { type: "image/jpeg" });
            addPendingFile(file);
            closeImageCamera();
        };

        const restoreDraft = async () => {
            if (!shouldRestoreDraft) {
                await clearDraft();
                return;
            }

            try {
                const draft = await vatTuDraftStore.get(draftKey);
                if (!draft || !Array.isArray(draft.pendingFiles) || draft.pendingFiles.length === 0) {
                    return;
                }

                draft.pendingFiles.forEach((draftFile) => {
                    if (!draftFile?.file) {
                        return;
                    }

                    const pendingItem = {
                        id: draftFile.id || `${Date.now()}-${Math.random().toString(16).slice(2)}`,
                        file: draftFile.file,
                        objectUrl: URL.createObjectURL(draftFile.file),
                        selection: ""
                    };

                    pendingFiles.push(pendingItem);
                    gallery.appendChild(buildPendingCard(pendingItem));
                });

                if (draft.selectedPrimary && typeof draft.selectedPrimary === "object") {
                    selectedPrimary = draft.selectedPrimary;
                }

                syncMasterInput();
                updatePrimaryPreview();
            } catch {
            }
        };

        sourceInputs.forEach((input) => {
            if (!(input instanceof HTMLInputElement)) {
                return;
            }

            input.addEventListener("change", () => {
                const files = Array.from(input.files || []);
                if (files.length === 0) {
                    return;
                }

                files.forEach(addPendingFile);
                input.value = "";
            });
        });

        gallery.addEventListener("click", (event) => {
            const removeExistingButton = event.target.closest("[data-image-remove-existing]");
            if (removeExistingButton instanceof HTMLElement) {
                const card = removeExistingButton.closest("[data-image-existing]");
                const imageUrl = card?.getAttribute("data-image-url");
                if (card instanceof HTMLElement && imageUrl) {
                    if (!removedExistingUrls.includes(imageUrl)) {
                        removedExistingUrls.push(imageUrl);
                        syncRemovedInputs();
                    }

                    if (selectedPrimary?.type === "existing" && selectedPrimary.key === imageUrl) {
                        selectedPrimary = null;
                    }

                    card.remove();
                    updatePrimaryPreview();
                }

                return;
            }

            const makePrimaryButton = event.target.closest("[data-image-make-primary]");
            if (makePrimaryButton instanceof HTMLElement) {
                const card = makePrimaryButton.closest("[data-image-item]");
                setSelectedPrimaryFromCard(card);
                updatePrimaryPreview();
                void persistDraft();
                return;
            }

            const removePendingButton = event.target.closest("[data-image-remove-pending]");
            if (removePendingButton instanceof HTMLElement) {
                const card = removePendingButton.closest("[data-file-id]");
                const fileId = card?.getAttribute("data-file-id");
                if (!(card instanceof HTMLElement) || !fileId) {
                    return;
                }

                const pendingIndex = pendingFiles.findIndex((pendingItem) => pendingItem.id === fileId);
                if (pendingIndex >= 0) {
                    URL.revokeObjectURL(pendingFiles[pendingIndex].objectUrl);
                    pendingFiles.splice(pendingIndex, 1);
                    syncMasterInput();
                }

                if (selectedPrimary?.type === "pending" && selectedPrimary.key === fileId) {
                    selectedPrimary = null;
                }

                card.remove();
                updatePrimaryPreview();
                void persistDraft();
                return;
            }

            const actionable = event.target.closest("[data-image-action]");
            if (actionable instanceof HTMLElement) {
                return;
            }

            const card = event.target.closest("[data-image-item]");
            const image = card?.querySelector("img");
            if (card instanceof HTMLElement && image instanceof HTMLImageElement) {
                openViewer(image.getAttribute("src") || "", image.getAttribute("alt") || "");
            }
        });

        viewerCloseButtons.forEach((button) => {
            button.addEventListener("click", closeViewer);
        });

        zoomInButton.addEventListener("click", () => {
            viewerScale = Math.min(4, viewerScale + 0.25);
            applyViewerTransform();
        });

        zoomOutButton.addEventListener("click", () => {
            viewerScale = Math.max(0.5, viewerScale - 0.25);
            applyViewerTransform();
        });

        rotateLeftButton.addEventListener("click", () => {
            viewerRotation -= 90;
            applyViewerTransform();
        });

        rotateRightButton.addEventListener("click", () => {
            viewerRotation += 90;
            applyViewerTransform();
        });

        resetViewButton.addEventListener("click", resetViewer);
        cameraOpenButton?.addEventListener("click", () => void openImageCamera());
        cameraCloseButtons.forEach((button) => button.addEventListener("click", closeImageCamera));
        cameraCaptureButton?.addEventListener("click", () => void captureImageCamera());
        cameraRetakeButton?.addEventListener("click", resetCameraCapture);
        cameraSaveButton?.addEventListener("click", saveCapturedImage);

        document.addEventListener("keydown", (event) => {
            if (event.key === "Escape" && cameraShell instanceof HTMLElement && !cameraShell.hidden) {
                closeImageCamera();
                return;
            }

            if (event.key === "Escape" && !viewer.hidden) {
                closeViewer();
            }
        });

        window.addEventListener("pagehide", () => {
            stopImageCamera();
            if (capturedCameraPreviewUrl) {
                URL.revokeObjectURL(capturedCameraPreviewUrl);
                capturedCameraPreviewUrl = "";
            }
            pendingFiles.forEach((pendingItem) => {
                URL.revokeObjectURL(pendingItem.objectUrl);
            });
            pendingFiles = [];
        });

        syncRemovedInputs();
        syncMasterInput();
        updatePrimaryPreview();
        void restoreDraft();
    });

    const activateTabInGroup = (group, tabName) => {
        const buttons = Array.from(group.querySelectorAll("[data-tab-button]"));
        const panels = Array.from(group.querySelectorAll("[data-tab-panel]"));
        const activeTabInput = group.querySelector("[data-active-tab-input]");

        if (buttons.length === 0 || panels.length === 0) {
            return;
        }

        buttons.forEach((button) => {
            const isActive = button.getAttribute("data-tab") === tabName;
            button.classList.toggle("is-active", isActive);
            button.setAttribute("aria-selected", String(isActive));
        });

        panels.forEach((panel) => {
            const isActive = panel.getAttribute("data-tab") === tabName;
            panel.classList.toggle("is-active", isActive);
            panel.hidden = !isActive;
        });

        if (activeTabInput instanceof HTMLInputElement) {
            activeTabInput.value = tabName;
        }
    };

    document.querySelectorAll("[data-tab-group]").forEach((group) => {
        const buttons = Array.from(group.querySelectorAll("[data-tab-button]"));
        const activeTabInput = group.querySelector("[data-active-tab-input]");

        if (buttons.length === 0) {
            return;
        }

        const initialTab = activeTabInput instanceof HTMLInputElement && activeTabInput.value
            ? activeTabInput.value
            : buttons[0]?.getAttribute("data-tab");

        if (initialTab) {
            activateTabInGroup(group, initialTab);
        }

        buttons.forEach((button) => {
            button.addEventListener("click", () => {
                const tabName = button.getAttribute("data-tab");
                if (!tabName) {
                    return;
                }

                activateTabInGroup(group, tabName);
            });
        });
    });

    const congViecChecklistDraftStorageKey = "cong-viec-checklist-draft:new";
    if (document.querySelector("[data-cong-viec-checklist-clear-draft]")) {
        try {
            localStorage.removeItem(congViecChecklistDraftStorageKey);
        } catch {
        }
    }

    document.querySelectorAll("[data-cong-viec-checklist-manager-root]").forEach((form) => {
        const manager = form.querySelector("[data-cong-viec-checklist-manager]");
        const list = manager?.querySelector("[data-checklist-list]");
        const emptyState = manager?.querySelector("[data-checklist-empty]");
        const addButton = manager?.querySelector("[data-checklist-add]");
        const template = manager?.querySelector("[data-checklist-template]");
        const draftEnabled = form.getAttribute("data-draft-enabled") === "true";

        if (!(manager instanceof HTMLElement) ||
            !(list instanceof HTMLElement) ||
            !(emptyState instanceof HTMLElement) ||
            !(addButton instanceof HTMLElement) ||
            !(template instanceof HTMLTemplateElement)) {
            return;
        }

        let draggingItem = null;

        const getItems = () => Array.from(list.querySelectorAll("[data-checklist-item]"));

        const updateEmptyState = () => {
            emptyState.classList.toggle("is-hidden", getItems().length > 0);
        };

        const syncItem = (item, index) => {
            const nameInput = item.querySelector("[data-checklist-name]");
            const positionInput = item.querySelector("[data-checklist-position]");
            const statusInput = item.querySelector("[data-checklist-status]");
            const enabledInput = item.querySelector("[data-checklist-enabled]");
            const positionLabel = item.querySelector("[data-checklist-position-label]");
            const prefix = `Form.DanhSachChecklist[${index}]`;

            if (nameInput instanceof HTMLInputElement) {
                nameInput.name = `${prefix}.TenChecklist`;
            }

            if (positionInput instanceof HTMLInputElement) {
                positionInput.name = `${prefix}.ViTri`;
                positionInput.value = `${index + 1}`;
            }

            if (statusInput instanceof HTMLInputElement) {
                statusInput.name = `${prefix}.TrangThaiSuDung`;
                statusInput.value = enabledInput instanceof HTMLInputElement && enabledInput.checked ? "true" : "false";
            }

            if (positionLabel instanceof HTMLElement) {
                positionLabel.textContent = `${index + 1}`;
            }
        };

        const syncAllItems = () => {
            const items = getItems();
            items.forEach((item, index) => {
                syncItem(item, index);
            });
            updateEmptyState();
            updateMoveButtons();
            return items;
        };

        const saveDraft = () => {
            if (!draftEnabled) {
                return;
            }

            try {
                const draftItems = getItems()
                    .map((item, index) => {
                        const nameInput = item.querySelector("[data-checklist-name]");
                        const enabledInput = item.querySelector("[data-checklist-enabled]");

                        return {
                            tenChecklist: nameInput instanceof HTMLInputElement ? nameInput.value.trim() : "",
                            viTri: index + 1,
                            trangThaiSuDung: enabledInput instanceof HTMLInputElement ? enabledInput.checked : true
                        };
                    })
                    .filter((item) => item.tenChecklist);

                if (draftItems.length === 0) {
                    localStorage.removeItem(congViecChecklistDraftStorageKey);
                    return;
                }

                localStorage.setItem(congViecChecklistDraftStorageKey, JSON.stringify(draftItems));
            } catch {
            }
        };

        const persistChecklistState = () => {
            syncAllItems();
            saveDraft();
        };

        const updateMoveButtons = () => {
            const items = getItems();
            items.forEach((item, index) => {
                const upButton = item.querySelector('[data-checklist-move="up"]');
                const downButton = item.querySelector('[data-checklist-move="down"]');

                if (upButton instanceof HTMLButtonElement) {
                    upButton.disabled = index === 0;
                }

                if (downButton instanceof HTMLButtonElement) {
                    downButton.disabled = index === items.length - 1;
                }
            });
        };

        const moveItem = (item, direction) => {
            if (!(item instanceof HTMLElement)) {
                return;
            }

            if (direction === "up") {
                const previousItem = item.previousElementSibling;
                if (previousItem instanceof HTMLElement) {
                    list.insertBefore(item, previousItem);
                    persistChecklistState();
                }
                return;
            }

            if (direction === "down") {
                const nextItem = item.nextElementSibling;
                if (nextItem instanceof HTMLElement) {
                    list.insertBefore(nextItem, item);
                    persistChecklistState();
                }
            }
        };

        const findItemAfterPointer = (clientY) => {
            const items = getItems().filter((item) => item !== draggingItem);
            let closestItem = null;
            let closestOffset = Number.NEGATIVE_INFINITY;

            items.forEach((item) => {
                const rect = item.getBoundingClientRect();
                const offset = clientY - rect.top - (rect.height / 2);
                if (offset < 0 && offset > closestOffset) {
                    closestOffset = offset;
                    closestItem = item;
                }
            });

            return closestItem;
        };

        const attachItemEvents = (item) => {
            const nameInput = item.querySelector("[data-checklist-name]");
            const enabledInput = item.querySelector("[data-checklist-enabled]");
            const removeButton = item.querySelector("[data-checklist-remove]");
            const dragHandle = item.querySelector("[data-checklist-drag-handle]");
            const moveUpButton = item.querySelector('[data-checklist-move="up"]');
            const moveDownButton = item.querySelector('[data-checklist-move="down"]');

            if (nameInput instanceof HTMLInputElement) {
                nameInput.addEventListener("input", () => {
                    saveDraft();
                });

                nameInput.addEventListener("change", () => {
                    saveDraft();
                });
            }

            if (enabledInput instanceof HTMLInputElement) {
                enabledInput.addEventListener("change", () => {
                    persistChecklistState();
                });
            }

            if (removeButton instanceof HTMLElement) {
                removeButton.addEventListener("click", () => {
                    item.remove();
                    persistChecklistState();
                });
            }

            if (dragHandle instanceof HTMLElement) {
                dragHandle.addEventListener("dragstart", (event) => {
                    draggingItem = item;
                    item.classList.add("is-dragging");

                    if (event.dataTransfer) {
                        event.dataTransfer.effectAllowed = "move";
                        event.dataTransfer.setData("text/plain", item.querySelector("[data-checklist-name]") instanceof HTMLInputElement
                            ? item.querySelector("[data-checklist-name]").value
                            : "checklist");
                    }
                });

                dragHandle.addEventListener("dragend", () => {
                    item.classList.remove("is-dragging");
                    draggingItem = null;
                    persistChecklistState();
                });
            }

            if (moveUpButton instanceof HTMLButtonElement) {
                moveUpButton.addEventListener("click", () => {
                    moveItem(item, "up");
                });
            }

            if (moveDownButton instanceof HTMLButtonElement) {
                moveDownButton.addEventListener("click", () => {
                    moveItem(item, "down");
                });
            }
        };

        const createChecklistItem = (source = {}) => {
            const fragment = template.content.cloneNode(true);
            const item = fragment.firstElementChild;
            if (!(item instanceof HTMLElement)) {
                return null;
            }

            const nameInput = item.querySelector("[data-checklist-name]");
            const enabledInput = item.querySelector("[data-checklist-enabled]");
            const statusInput = item.querySelector("[data-checklist-status]");

            if (nameInput instanceof HTMLInputElement) {
                nameInput.value = typeof source.tenChecklist === "string" ? source.tenChecklist : "";
            }

            if (enabledInput instanceof HTMLInputElement) {
                const isEnabled = source.trangThaiSuDung !== false;
                enabledInput.checked = isEnabled;
            }

            if (statusInput instanceof HTMLInputElement) {
                statusInput.value = source.trangThaiSuDung === false ? "false" : "true";
            }

            attachItemEvents(item);
            return item;
        };

        addButton.addEventListener("click", () => {
            const item = createChecklistItem();
            if (!(item instanceof HTMLElement)) {
                return;
            }

            list.appendChild(item);
            persistChecklistState();

            const nameInput = item.querySelector("[data-checklist-name]");
            if (nameInput instanceof HTMLInputElement) {
                requestAnimationFrame(() => {
                    nameInput.focus();
                });
            }
        });

        list.addEventListener("dragover", (event) => {
            if (!(draggingItem instanceof HTMLElement)) {
                return;
            }

            event.preventDefault();
            const itemAfterPointer = findItemAfterPointer(event.clientY);
            if (itemAfterPointer instanceof HTMLElement) {
                list.insertBefore(draggingItem, itemAfterPointer);
                return;
            }

            list.appendChild(draggingItem);
        });

        list.addEventListener("drop", (event) => {
            if (!(draggingItem instanceof HTMLElement)) {
                return;
            }

            event.preventDefault();
            persistChecklistState();
        });

        getItems().forEach((item) => {
            attachItemEvents(item);
        });

        if (draftEnabled && getItems().length === 0) {
            try {
                const rawDraft = localStorage.getItem(congViecChecklistDraftStorageKey);
                if (rawDraft) {
                    const draftItems = JSON.parse(rawDraft);
                    if (Array.isArray(draftItems)) {
                        draftItems.forEach((draftItem) => {
                            const item = createChecklistItem(draftItem);
                            if (item instanceof HTMLElement) {
                                list.appendChild(item);
                            }
                        });
                    }
                }
            } catch {
            }
        }

        form.addEventListener("submit", () => {
            persistChecklistState();
        });

        persistChecklistState();
    });

    const loadHtml5Qrcode = (() => {
        let promise = null;

        return () => {
            if (window.Html5Qrcode) {
                return Promise.resolve(window.Html5Qrcode);
            }

            if (promise) {
                return promise;
            }

            promise = new Promise((resolve, reject) => {
                const script = document.createElement("script");
                script.src = "/lib/html5-qrcode/html5-qrcode.min.js";
                script.async = true;
                script.onload = () => {
                    if (window.Html5Qrcode) {
                        resolve(window.Html5Qrcode);
                        return;
                    }

                    reject(new Error("Html5Qrcode is unavailable."));
                };
                script.onerror = () => reject(new Error("Failed to load html5-qrcode."));
                document.head.appendChild(script);
            });

            return promise;
        };
    })();

    const loadNimiqQrScanner = (() => {
        let promise = null;

        return () => {
            if (window.ApptechQrScanner) {
                return Promise.resolve(window.ApptechQrScanner);
            }

            if (promise) {
                return promise;
            }

            promise = import("/lib/qr-scanner/qr-scanner.min.js")
                .then((module) => {
                    const QrScanner = module.default || module.QrScanner || module;
                    if (!QrScanner) {
                        throw new Error("QrScanner is unavailable.");
                    }

                    QrScanner.WORKER_PATH = "/lib/qr-scanner/qr-scanner-worker.min.js";
                    window.ApptechQrScanner = QrScanner;
                    return QrScanner;
                });

            return promise;
        };
    })();

    const loadJsQr = (() => {
        let promise = null;

        return () => {
            if (window.jsQR) {
                return Promise.resolve(window.jsQR);
            }

            if (promise) {
                return promise;
            }

            promise = new Promise((resolve, reject) => {
                const script = document.createElement("script");
                script.src = "/lib/jsqr/jsQR.min.js";
                script.async = true;
                script.onload = () => {
                    if (window.jsQR) {
                        resolve(window.jsQR);
                        return;
                    }

                    reject(new Error("jsQR is unavailable."));
                };
                script.onerror = () => reject(new Error("Failed to load jsQR."));
                document.head.appendChild(script);
            });

            return promise;
        };
    })();

    const getQrFormatsToSupport = () => {
        const supportedFormats = window.Html5QrcodeSupportedFormats;
        if (!supportedFormats || typeof supportedFormats.QR_CODE === "undefined") {
            return undefined;
        }

        return [supportedFormats.QR_CODE];
    };

    const createNimiqQrCodeInstance = (readerId) => {
        let scanner = null;
        let video = null;
        let isStarted = false;

        const getReader = () => document.getElementById(readerId);
        const getTrack = () => {
            const stream = video?.srcObject;
            return stream instanceof MediaStream ? stream.getVideoTracks()[0] || null : null;
        };

        const getPreferredCamera = (cameraTarget) => {
            if (typeof cameraTarget === "string" && cameraTarget) {
                return cameraTarget;
            }

            const facingMode = cameraTarget?.facingMode;
            if (typeof facingMode === "string") {
                return facingMode;
            }

            return facingMode?.ideal || facingMode?.exact || "environment";
        };

        const withTimeout = (promise, timeoutMs, message) => {
            let timeoutId = 0;
            const timeout = new Promise((_, reject) => {
                timeoutId = window.setTimeout(() => reject(new Error(message)), timeoutMs);
            });

            return Promise.race([promise, timeout]).finally(() => {
                if (timeoutId) {
                    window.clearTimeout(timeoutId);
                }
            });
        };

        const logVideoState = (eventName) => {
            appendQrDebugLog(eventName, {
                readyState: video?.readyState ?? null,
                videoWidth: video?.videoWidth ?? null,
                videoHeight: video?.videoHeight ?? null,
                paused: video?.paused ?? null,
                ended: video?.ended ?? null,
                settings: getTrack()?.getSettings?.() || null
            });
        };

        return {
            engine: "nimiq",
            async start(cameraTarget, _config, onScanSuccess, onScanFailure) {
                const QrScanner = await loadNimiqQrScanner();
                const reader = getReader();
                if (!(reader instanceof HTMLElement)) {
                    throw new Error("QR reader element not found.");
                }

                if (scanner) {
                    await this.clear();
                }

                const preferredCamera = getPreferredCamera(cameraTarget);
                const cameraAttempts = [...new Set([preferredCamera, "environment"])];
                let lastError = null;

                for (const camera of cameraAttempts) {
                    await this.clear();
                    reader.innerHTML = "";
                    video = document.createElement("video");
                    video.className = "qr-nimiq-video";
                    video.setAttribute("playsinline", "true");
                    video.setAttribute("webkit-playsinline", "true");
                    video.setAttribute("autoplay", "true");
                    video.setAttribute("muted", "true");
                    video.muted = true;
                    video.addEventListener("loadedmetadata", () => logVideoState("qr_nimiq_video_loadedmetadata"), { once: true });
                    video.addEventListener("playing", () => logVideoState("qr_nimiq_video_playing"), { once: true });
                    video.addEventListener("error", () => logVideoState("qr_nimiq_video_error"), { once: true });
                    reader.appendChild(video);

                    appendQrDebugLog("qr_nimiq_create", {
                        readerId,
                        preferredCamera: camera
                    });

                    scanner = new QrScanner(
                        video,
                        (result) => {
                            const decodedText = typeof result === "string" ? result : result?.data;
                            if (decodedText) {
                                onScanSuccess(decodedText, result);
                            }
                        },
                        {
                            preferredCamera: camera,
                            maxScansPerSecond: 25,
                            returnDetailedScanResult: true,
                            highlightScanRegion: false,
                            highlightCodeOutline: false,
                            calculateScanRegion: (sourceVideo) => {
                                const width = sourceVideo.videoWidth || sourceVideo.clientWidth || 640;
                                const height = sourceVideo.videoHeight || sourceVideo.clientHeight || 480;
                                return {
                                    x: 0,
                                    y: 0,
                                    width,
                                    height,
                                    downScaledWidth: Math.min(900, width),
                                    downScaledHeight: Math.round(Math.min(900, width) * height / width)
                                };
                            },
                            onDecodeError: (error) => {
                                if (typeof onScanFailure === "function") {
                                    onScanFailure(error);
                                }
                            }
                        });

                    if (typeof scanner.setInversionMode === "function") {
                        scanner.setInversionMode("both");
                    }

                    try {
                        await withTimeout(scanner.start(), 6500, "Nimiq QR scanner start timed out.");
                        isStarted = true;
                        logVideoState("qr_nimiq_after_start");
                        if (video instanceof HTMLVideoElement && video.paused) {
                            await video.play().catch((error) => {
                                appendQrDebugLog("qr_nimiq_video_play_error", {
                                    name: error?.name || "",
                                    message: error?.message || `${error || ""}`
                                });
                            });
                        }

                        appendQrDebugLog("qr_nimiq_start_success", {
                            preferredCamera: camera,
                            settings: this.getRunningTrackSettings()
                        });
                        return;
                    } catch (error) {
                        lastError = error;
                        appendQrDebugLog("qr_nimiq_start_error", {
                            preferredCamera: camera,
                            name: error?.name || "",
                            message: error?.message || `${error || ""}`
                        });
                    }
                }

                await this.clear();
                throw lastError || new Error("Nimiq QR scanner failed to start.");
            },
            async stop() {
                if (scanner) {
                    await scanner.stop();
                }
                isStarted = false;
            },
            async clear() {
                if (scanner) {
                    scanner.destroy();
                    scanner = null;
                }
                isStarted = false;

                if (video) {
                    video.srcObject = null;
                    video.remove();
                    video = null;
                }
            },
            getState() {
                return isStarted ? 2 : 1;
            },
            getRunningTrackSettings() {
                try {
                    return getTrack()?.getSettings?.() || null;
                } catch {
                    return null;
                }
            },
            getRunningTrackCapabilities() {
                try {
                    return getTrack()?.getCapabilities?.() || null;
                } catch {
                    return null;
                }
            },
            async applyVideoConstraints(constraints) {
                const track = getTrack();
                if (!track?.applyConstraints) {
                    return false;
                }

                await track.applyConstraints(constraints);
                return true;
            },
            async scanFile(file) {
                const QrScanner = await loadNimiqQrScanner();
                const result = await QrScanner.scanImage(file, {
                    returnDetailedScanResult: true,
                    alsoTryWithoutScanRegion: true
                });
                return typeof result === "string" ? result : result?.data;
            },
            async scanFileV2(file) {
                const data = await this.scanFile(file);
                return { decodedText: data };
            }
        };
    };

    const createJsQrCodeInstance = (readerId) => {
        let stream = null;
        let video = null;
        let canvas = null;
        let context = null;
        let scanTimer = 0;
        let isStarted = false;
        let isStopped = true;

        const getReader = () => document.getElementById(readerId);
        const getTrack = () => stream?.getVideoTracks?.()[0] || null;
        const clearScanTimer = () => {
            if (scanTimer) {
                window.clearTimeout(scanTimer);
                scanTimer = 0;
            }
        };

        const buildVideoConstraints = (cameraTarget) => {
            if (typeof cameraTarget === "string" && cameraTarget) {
                return {
                    deviceId: { ideal: cameraTarget },
                    facingMode: { ideal: "environment" },
                    width: { ideal: 1280 },
                    height: { ideal: 720 },
                    frameRate: { ideal: 30, max: 30 }
                };
            }

            return {
                facingMode: { ideal: "environment" },
                width: { ideal: 1280 },
                height: { ideal: 720 },
                frameRate: { ideal: 30, max: 30 }
            };
        };

        const waitForVideoEvent = (eventName, timeoutMs) => new Promise((resolve, reject) => {
            if (!video) {
                reject(new Error("Video element was removed."));
                return;
            }

            let timeoutId = 0;
            const cleanup = () => {
                video?.removeEventListener(eventName, onEvent);
                video?.removeEventListener("error", onError);
                if (timeoutId) {
                    window.clearTimeout(timeoutId);
                }
            };
            const onEvent = () => {
                cleanup();
                resolve();
            };
            const onError = () => {
                cleanup();
                reject(new Error(`Video ${eventName} failed.`));
            };

            video.addEventListener(eventName, onEvent, { once: true });
            video.addEventListener("error", onError, { once: true });
            timeoutId = window.setTimeout(() => {
                cleanup();
                reject(new Error(`Timed out waiting for video ${eventName}.`));
            }, timeoutMs);
        });

        const waitForVideoReady = () => new Promise((resolve, reject) => {
            const startedAt = Date.now();
            const tick = () => {
                if (!video) {
                    reject(new Error("Video element was removed."));
                    return;
                }

                if (video.videoWidth > 0 && video.videoHeight > 0 && video.readyState >= HTMLMediaElement.HAVE_CURRENT_DATA) {
                    resolve();
                    return;
                }

                if (Date.now() - startedAt > 6500) {
                    reject(new Error("iOS video did not become ready."));
                    return;
                }

                window.setTimeout(tick, 120);
            };

            tick();
        });

        const playVideoWithTimeout = async () => {
            if (!video) {
                throw new Error("Video element was removed.");
            }

            const playPromise = video.play();
            if (!playPromise || typeof playPromise.then !== "function") {
                return;
            }

            let timeoutId = 0;
            await Promise.race([
                playPromise,
                new Promise((_, reject) => {
                    timeoutId = window.setTimeout(() => reject(new Error("Timed out waiting for video.play().")), 4500);
                })
            ]).finally(() => {
                if (timeoutId) {
                    window.clearTimeout(timeoutId);
                }
            });
        };

        const logVideoState = (eventName) => {
            appendQrDebugLog(eventName, {
                readyState: video?.readyState ?? null,
                videoWidth: video?.videoWidth ?? null,
                videoHeight: video?.videoHeight ?? null,
                clientWidth: video?.clientWidth ?? null,
                clientHeight: video?.clientHeight ?? null,
                paused: video?.paused ?? null,
                settings: getTrack()?.getSettings?.() || null
            });
        };

        const scanFrame = async (jsQR, onScanSuccess, onScanFailure) => {
            if (isStopped || !video || !canvas || !context) {
                return;
            }

            try {
                const sourceWidth = video.videoWidth || 0;
                const sourceHeight = video.videoHeight || 0;
                if (sourceWidth > 0 && sourceHeight > 0) {
                    const maxWidth = 960;
                    const scale = Math.min(1, maxWidth / sourceWidth);
                    const width = Math.max(1, Math.round(sourceWidth * scale));
                    const height = Math.max(1, Math.round(sourceHeight * scale));

                    if (canvas.width !== width || canvas.height !== height) {
                        canvas.width = width;
                        canvas.height = height;
                    }

                    context.drawImage(video, 0, 0, width, height);
                    const imageData = context.getImageData(0, 0, width, height);
                    const result = jsQR(imageData.data, width, height, {
                        inversionAttempts: "attemptBoth"
                    });

                    if (result?.data) {
                        appendQrDebugLog("qr_jsqr_decode_success", {
                            width,
                            height,
                            dataLength: result.data.length
                        });
                        onScanSuccess(result.data, result);
                        return;
                    }
                }
            } catch (error) {
                appendQrDebugLog("qr_jsqr_decode_error", {
                    name: error?.name || "",
                    message: error?.message || `${error || ""}`
                });
                if (typeof onScanFailure === "function") {
                    onScanFailure(error);
                }
            }

            scanTimer = window.setTimeout(() => {
                void scanFrame(jsQR, onScanSuccess, onScanFailure);
            }, 90);
        };

        return {
            engine: "jsqr-ios",
            async start(cameraTarget, _config, onScanSuccess, onScanFailure) {
                const jsQR = await loadJsQr();
                const reader = getReader();
                if (!(reader instanceof HTMLElement)) {
                    throw new Error("QR reader element not found.");
                }

                await this.clear();
                reader.innerHTML = "";
                video = document.createElement("video");
                video.className = "qr-nimiq-video";
                video.setAttribute("playsinline", "true");
                video.setAttribute("webkit-playsinline", "true");
                video.setAttribute("autoplay", "true");
                video.setAttribute("muted", "true");
                video.playsInline = true;
                video.autoplay = true;
                video.muted = true;
                video.controls = false;
                video.addEventListener("loadedmetadata", () => logVideoState("qr_jsqr_video_loadedmetadata"), { once: true });
                video.addEventListener("playing", () => logVideoState("qr_jsqr_video_playing"), { once: true });
                video.addEventListener("error", () => logVideoState("qr_jsqr_video_error"), { once: true });
                reader.appendChild(video);

                const constraints = {
                    video: buildVideoConstraints(cameraTarget),
                    audio: false
                };
                appendQrDebugLog("qr_jsqr_getusermedia_start", constraints);
                stream = await navigator.mediaDevices.getUserMedia(constraints);
                appendQrDebugLog("qr_jsqr_getusermedia_success", {
                    settings: getTrack()?.getSettings?.() || null
                });

                appendQrDebugLog("qr_jsqr_attach_stream_before");
                video.srcObject = stream;
                video.load();
                appendQrDebugLog("qr_jsqr_attach_stream_after");
                try {
                    await waitForVideoEvent("loadedmetadata", 3500);
                } catch (error) {
                    appendQrDebugLog("qr_jsqr_wait_loadedmetadata_error", {
                        name: error?.name || "",
                        message: error?.message || `${error || ""}`
                    });
                }

                appendQrDebugLog("qr_jsqr_play_before");
                await playVideoWithTimeout();
                appendQrDebugLog("qr_jsqr_play_after");
                await waitForVideoReady();
                logVideoState("qr_jsqr_after_video_ready");

                canvas = document.createElement("canvas");
                context = canvas.getContext("2d", { willReadFrequently: true });
                if (!context) {
                    throw new Error("Cannot create QR canvas context.");
                }

                isStopped = false;
                isStarted = true;
                void scanFrame(jsQR, onScanSuccess, onScanFailure);
            },
            async stop() {
                isStopped = true;
                isStarted = false;
                clearScanTimer();
                if (stream) {
                    stream.getTracks().forEach((track) => track.stop());
                    stream = null;
                }

                if (video) {
                    video.pause();
                    video.srcObject = null;
                }
            },
            async clear() {
                await this.stop();
                if (video) {
                    video.remove();
                    video = null;
                }
                canvas = null;
                context = null;
            },
            getState() {
                return isStarted ? 2 : 1;
            },
            getRunningTrackSettings() {
                return getTrack()?.getSettings?.() || null;
            },
            getRunningTrackCapabilities() {
                return getTrack()?.getCapabilities?.() || null;
            },
            async applyVideoConstraints(constraints) {
                const track = getTrack();
                if (!track?.applyConstraints) {
                    return false;
                }
                await track.applyConstraints(constraints);
                return true;
            },
            async scanFile(file) {
                const jsQR = await loadJsQr();
                const bitmap = await createImageBitmap(file);
                const localCanvas = document.createElement("canvas");
                localCanvas.width = bitmap.width;
                localCanvas.height = bitmap.height;
                const localContext = localCanvas.getContext("2d", { willReadFrequently: true });
                if (!localContext) {
                    throw new Error("Cannot read selected QR image.");
                }
                localContext.drawImage(bitmap, 0, 0);
                const imageData = localContext.getImageData(0, 0, bitmap.width, bitmap.height);
                const result = jsQR(imageData.data, bitmap.width, bitmap.height, {
                    inversionAttempts: "attemptBoth"
                });
                if (!result?.data) {
                    throw new Error("No QR code found in selected image.");
                }
                return result.data;
            },
            async scanFileV2(file) {
                const data = await this.scanFile(file);
                return { decodedText: data };
            }
        };
    };

    const createHtml5QrCodeInstance = (readerId) => {
        if (isAppleMobileDevice()) {
            return createJsQrCodeInstance(readerId);
        }

        const fullConfig = {
            verbose: false,
            useBarCodeDetectorIfSupported: true
        };
        const formatsToSupport = getQrFormatsToSupport();

        if (Array.isArray(formatsToSupport) && formatsToSupport.length > 0) {
            fullConfig.formatsToSupport = formatsToSupport;
        }

        return new window.Html5Qrcode(readerId, fullConfig);
    };

    const clampNumber = (value, min, max) => Math.min(Math.max(value, min), max);
    const qrScannerTuningVersion = "QR iOS jsQR v20260513.23";
    const qrDebugLogs = [];

    const appendQrDebugLog = (eventName, data = null) => {
        const entry = {
            time: new Date().toISOString(),
            event: eventName,
            data
        };
        qrDebugLogs.push(entry);
        if (qrDebugLogs.length > 250) {
            qrDebugLogs.shift();
        }
        return entry;
    };

    const formatQrDebugLogs = () => qrDebugLogs
        .map((entry) => `[${entry.time}] ${entry.event}${entry.data ? ` ${JSON.stringify(entry.data)}` : ""}`)
        .join("\n");

    const isAppleMobileDevice = () => /iPad|iPhone|iPod/.test(navigator.userAgent) ||
        (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);

    const getQrScannerProfile = () => {
        const isApple = isAppleMobileDevice();
        return {
            isApple,
            fps: isApple ? 24 : 24,
            qrboxRatio: isApple ? 0.92 : 0.985,
            minQrboxSize: isApple ? 260 : 260,
            maxQrboxSize: isApple ? 520 : 500,
            videoWidth: isApple ? 3840 : 1920,
            videoHeight: isApple ? 2160 : 1080,
            zoom: isApple ? 3.0 : 1.9
        };
    };

    const createQrCameraConfig = () => {
        const profile = getQrScannerProfile();
        if (profile.isApple) {
            return {
                fps: 10,
                qrbox: { width: 220, height: 220 },
                disableFlip: true,
                videoConstraints: {
                    width: { ideal: 1280 },
                    height: { ideal: 720 },
                    frameRate: { ideal: 30, max: 30 }
                }
            };
        }

        const config = {
            fps: profile.fps,
            disableFlip: false
        };

        config.qrbox = (viewfinderWidth, viewfinderHeight) => {
            if (!Number.isFinite(viewfinderWidth) || !Number.isFinite(viewfinderHeight)) {
                return { width: 300, height: 300 };
            }

            const minEdge = Math.min(viewfinderWidth, viewfinderHeight);
            const size = Math.max(
                profile.minQrboxSize,
                Math.min(Math.floor(minEdge * profile.qrboxRatio), minEdge - 2, profile.maxQrboxSize));
            return {
                width: Math.min(size, minEdge),
                height: Math.min(size, minEdge)
            };
        };
        if (!profile.isApple) {
            config.aspectRatio = 1;
        }

        return config;
    };

    const createQrCameraTargets = (cameraId) => {
        const profile = getQrScannerProfile();
        if (!profile.isApple) {
            return [cameraId || { facingMode: { ideal: "environment" } }];
        }

        return [cameraId || { facingMode: "environment" }];
    };

    const getPreferredQrCamera = (cameras) => {
        const options = Array.isArray(cameras) ? cameras : [];
        if (options.length === 0) {
            return null;
        }

        const normalizeCameraLabel = (value) => `${value || ""}`
            .normalize("NFD")
            .replace(/[\u0300-\u036f]/g, "")
            .toLowerCase();

        const scoreCamera = (camera, index) => {
            const label = normalizeCameraLabel(camera?.label);
            let score = Math.max(0, 100 - index);

            if (label.includes("back") ||
                label.includes("rear") ||
                label.includes("environment") ||
                label.includes("mat sau") ||
                label.includes("camera sau") ||
                label === "sau" ||
                label.includes(" sau")) {
                score += 200;
            }

            if (label.includes("wide")) {
                score += 20;
            }

            if (label.includes("dual") || label.includes("triple")) {
                score += 12;
            }

            if (label.includes("front") ||
                label.includes("user") ||
                label.includes("mat truoc") ||
                label.includes("camera truoc") ||
                label === "truoc" ||
                label.includes(" truoc")) {
                score -= 240;
            }

            if (label.includes("ultra") || label.includes("0.5") || label.includes("0,5")) {
                score -= 180;
            }

            if (label.includes("telephoto") || label.includes("tele")) {
                score -= 40;
            }

            return score;
        };

        return options
            .map((camera, index) => ({ camera, score: scoreCamera(camera, index) }))
            .sort((left, right) => right.score - left.score)[0]?.camera || options[0];
    };

    const getQrCameraLabel = (cameraId, cameras = []) => {
        const camera = Array.isArray(cameras)
            ? cameras.find((item) => item.id === cameraId)
            : null;
        return `${camera?.label || ""}`.trim();
    };

    const summarizeQrCameras = (cameras = []) => (Array.isArray(cameras) ? cameras : []).map((camera, index) => ({
        index,
        id: camera?.id || "",
        label: camera?.label || ""
    }));

    const applyQrVideoElementAttributes = (reader) => {
        const video = reader instanceof HTMLElement ? reader.querySelector("video") : null;
        if (!(video instanceof HTMLVideoElement)) {
            return;
        }

        video.setAttribute("playsinline", "true");
        video.setAttribute("webkit-playsinline", "true");
        video.setAttribute("autoplay", "true");
        video.setAttribute("muted", "true");
        video.removeAttribute("controls");
    };

    const getQrPointerPoint = (event, element) => {
        if (!(element instanceof HTMLElement)) {
            return { x: 0.5, y: 0.5 };
        }

        const rect = element.getBoundingClientRect();
        if (!rect.width || !rect.height) {
            return { x: 0.5, y: 0.5 };
        }

        return {
            x: clampNumber((event.clientX - rect.left) / rect.width, 0, 1),
            y: clampNumber((event.clientY - rect.top) / rect.height, 0, 1)
        };
    };

    const applyQrFocusHints = async (html5QrCode, point = { x: 0.5, y: 0.5 }) => {
        const constraints = {
            advanced: [
                {
                    focusMode: "continuous",
                    exposureMode: "continuous",
                    whiteBalanceMode: "continuous",
                    pointsOfInterest: [point]
                }
            ]
        };

        return tryApplyQrVideoConstraints(html5QrCode, constraints);
    };

    const setupQrTapToFocus = (html5QrCode, reader) => {
        const panel = reader instanceof HTMLElement ? reader.closest(".qr-scanner-panel") : null;
        if (!(panel instanceof HTMLElement) || panel.dataset.tapFocusReady === "true") {
            return;
        }

        panel.dataset.tapFocusReady = "true";
        panel.addEventListener("click", (event) => {
            if (!isAppleMobileDevice()) {
                return;
            }

            const point = getQrPointerPoint(event, panel);
            appendQrDebugLog("qr_tap_focus", point);
            void applyQrFocusHints(html5QrCode, point);
        });
    };

    const ensureQrScannerFrame = (reader) => {
        const panel = reader instanceof HTMLElement ? reader.closest(".qr-scanner-panel") : null;
        if (!(panel instanceof HTMLElement)) {
            return;
        }

        panel.classList.toggle("is-ios-qr-mode", isAppleMobileDevice());

        if (!panel.querySelector(".qr-scanner-frame")) {
            const frame = document.createElement("div");
            frame.className = "qr-scanner-frame";
            frame.setAttribute("aria-hidden", "true");
            panel.appendChild(frame);
        }

        let version = panel.querySelector(".qr-scanner-version");
        if (!(version instanceof HTMLElement)) {
            version = document.createElement("div");
            version.className = "qr-scanner-version";
            version.setAttribute("aria-hidden", "true");
            panel.appendChild(version);
        }

        const profile = getQrScannerProfile();
        version.textContent = `${qrScannerTuningVersion} | zoom ${profile.zoom}x`;

        const logHost = panel.closest(".qr-scanner-shell") || panel.parentElement || panel;
        let logPanel = logHost.querySelector(".qr-scanner-log-panel");
        if (!(logPanel instanceof HTMLElement)) {
            logPanel = document.createElement("div");
            logPanel.className = "qr-scanner-log-panel";
            logHost.appendChild(logPanel);
        }

        let focusHint = logPanel.querySelector(".qr-scanner-focus-hint");
        if (!(focusHint instanceof HTMLElement)) {
            focusHint = document.createElement("div");
            focusHint.className = "qr-scanner-focus-hint";
            focusHint.textContent = "iOS: chạm vào vùng QR để lấy nét nếu hình bị mờ.";
            logPanel.appendChild(focusHint);
        }
        focusHint.hidden = !isAppleMobileDevice();

        let logButton = logPanel.querySelector(".qr-scanner-log-button");
        if (!(logButton instanceof HTMLButtonElement)) {
            logButton = document.createElement("button");
            logButton.type = "button";
            logButton.className = "qr-scanner-log-button";
            logButton.textContent = "Copy log";
            logPanel.appendChild(logButton);
            logButton.addEventListener("click", async () => {
                const text = formatQrDebugLogs();
                try {
                    await navigator.clipboard.writeText(text);
                    logButton.textContent = "Copied";
                    window.setTimeout(() => {
                        logButton.textContent = "Copy log";
                    }, 1200);
                } catch {
                    const area = document.createElement("textarea");
                    area.value = text;
                    area.setAttribute("readonly", "true");
                    area.style.position = "fixed";
                    area.style.left = "-9999px";
                    document.body.appendChild(area);
                    area.select();
                    document.execCommand("copy");
                    area.remove();
                    logButton.textContent = "Copied";
                    window.setTimeout(() => {
                        logButton.textContent = "Copy log";
                    }, 1200);
                }
            });
        }
    };

    const startQrScannerCamera = async (html5QrCode, reader, cameraId, onScanSuccess, onScanFailure = () => {}) => {
        ensureQrScannerFrame(reader);
        const cameraTargets = createQrCameraTargets(cameraId);
        const config = createQrCameraConfig();
        const cameraTarget = cameraTargets[0];
        appendQrDebugLog("qr_start", {
            version: qrScannerTuningVersion,
            isApple: isAppleMobileDevice(),
            engine: html5QrCode?.engine || "html5-qrcode",
            cameraId,
            cameraTarget,
            config,
            profile: getQrScannerProfile(),
            userAgent: navigator.userAgent,
            platform: navigator.platform
        });

        try {
            await html5QrCode.start(
                cameraTarget,
                config,
                onScanSuccess,
                onScanFailure);
            appendQrDebugLog("qr_start_success", { cameraTarget });
        } catch (error) {
            appendQrDebugLog("qr_start_error", {
                cameraTarget,
                name: error?.name || "",
                message: error?.message || `${error || ""}`
            });
            throw error;
        }

        applyQrVideoElementAttributes(reader);
        setupQrTapToFocus(html5QrCode, reader);
        const runningSettings = await applyQrVideoEnhancements(html5QrCode);
        window.setTimeout(() => {
            void applyQrFocusHints(html5QrCode);
        }, 700);
        window.setTimeout(() => {
            void applyQrFocusHints(html5QrCode);
        }, 1600);
        appendQrDebugLog("qr_running_settings", runningSettings);
        const version = reader instanceof HTMLElement
            ? reader.closest(".qr-scanner-panel")?.querySelector(".qr-scanner-version")
            : null;
        if (version instanceof HTMLElement) {
            const profile = getQrScannerProfile();
            version.textContent = `${qrScannerTuningVersion} | target ${profile.zoom}x | actual ${runningSettings?.zoom ?? "n/a"}x`;
        }
    };

    const getQrScannerStateName = (html5QrCode) => {
        try {
            return typeof html5QrCode?.getState === "function"
                ? `${html5QrCode.getState()}`
                : "unknown";
        } catch (error) {
            return `error:${error?.message || error || ""}`;
        }
    };

    const resetQrScannerInstance = async (html5QrCode, scope = "qr") => {
        if (!html5QrCode) {
            return;
        }

        appendQrDebugLog("qr_reset_before_start", {
            scope,
            state: getQrScannerStateName(html5QrCode)
        });

        try {
            await html5QrCode.stop();
            appendQrDebugLog("qr_reset_stop_success", { scope });
        } catch (error) {
            appendQrDebugLog("qr_reset_stop_error", {
                scope,
                name: error?.name || "",
                message: error?.message || `${error || ""}`
            });
        }

        try {
            await html5QrCode.clear();
            appendQrDebugLog("qr_reset_clear_success", { scope });
        } catch (error) {
            appendQrDebugLog("qr_reset_clear_error", {
                scope,
                name: error?.name || "",
                message: error?.message || `${error || ""}`
            });
        }
    };

    const tryApplyQrVideoConstraints = async (html5QrCode, constraints) => {
        if (!html5QrCode || typeof html5QrCode.applyVideoConstraints !== "function") {
            return false;
        }

        try {
            await html5QrCode.applyVideoConstraints(constraints);
            appendQrDebugLog("qr_apply_constraints_success", constraints);
            return true;
        } catch (error) {
            appendQrDebugLog("qr_apply_constraints_error", {
                constraints,
                name: error?.name || "",
                message: error?.message || `${error || ""}`
            });
            return false;
        }
    };

    const applyQrVideoEnhancements = async (html5QrCode) => {
        if (!html5QrCode) {
            return null;
        }

        const profile = getQrScannerProfile();
        let zoomApplied = false;

        if (profile.isApple) {
            const currentSettings = typeof html5QrCode.getRunningTrackSettings === "function"
                ? html5QrCode.getRunningTrackSettings()
                : null;
            const currentWidth = Number(currentSettings?.width ?? 0);
            const currentHeight = Number(currentSettings?.height ?? 0);
            appendQrDebugLog("qr_ios_resolution_before_tune", currentSettings);

            if (Math.max(currentWidth, currentHeight) < 1000) {
                await tryApplyQrVideoConstraints(html5QrCode, {
                    width: { ideal: 1280 },
                    height: { ideal: 720 },
                    frameRate: { ideal: 30, max: 30 }
                });
            }
        } else {
            await tryApplyQrVideoConstraints(html5QrCode, {
                width: { ideal: profile.videoWidth },
                height: { ideal: profile.videoHeight },
                frameRate: { ideal: 30, max: 30 }
            });
        }

        await applyQrFocusHints(html5QrCode);

        try {
            const cameraCapabilities = typeof html5QrCode.getRunningTrackCameraCapabilities === "function"
                ? html5QrCode.getRunningTrackCameraCapabilities()
                : null;
            const zoomFeature = cameraCapabilities && typeof cameraCapabilities.zoomFeature === "function"
                ? cameraCapabilities.zoomFeature()
                : null;

            if (zoomFeature && typeof zoomFeature.isSupported === "function" && zoomFeature.isSupported()) {
                const min = Number(zoomFeature.min?.() ?? 1);
                const max = Number(zoomFeature.max?.() ?? min);
                const target = clampNumber(profile.zoom, min, max);
                appendQrDebugLog("qr_zoom_feature", { min, max, target });

                if (Number.isFinite(target) && target > min + 0.05) {
                    await zoomFeature.apply(target);
                    zoomApplied = true;
                    appendQrDebugLog("qr_zoom_feature_applied", { target });
                }
            }
        } catch (error) {
            appendQrDebugLog("qr_zoom_feature_error", {
                name: error?.name || "",
                message: error?.message || `${error || ""}`
            });
        }

        if (!zoomApplied) {
            try {
                const capabilities = typeof html5QrCode.getRunningTrackCapabilities === "function"
                    ? html5QrCode.getRunningTrackCapabilities()
                    : null;
                const min = Number(capabilities?.zoom?.min ?? Number.NaN);
                const max = Number(capabilities?.zoom?.max ?? Number.NaN);
                appendQrDebugLog("qr_track_capabilities", capabilities || null);

                if (Number.isFinite(min) && Number.isFinite(max) && max > min) {
                    const target = clampNumber(profile.zoom, min, max);
                    if (target > min + 0.05) {
                        await tryApplyQrVideoConstraints(html5QrCode, {
                            advanced: [{ zoom: target }]
                        });
                    }
                }
            } catch (error) {
                appendQrDebugLog("qr_track_capabilities_error", {
                    name: error?.name || "",
                    message: error?.message || `${error || ""}`
                });
            }
        }

        try {
            return typeof html5QrCode.getRunningTrackSettings === "function"
                ? html5QrCode.getRunningTrackSettings()
                : null;
        } catch {
            return null;
        }
    };

    const scanQrFromImageFile = async (html5QrCode, file) => {
        if (html5QrCode && typeof html5QrCode.scanFileV2 === "function") {
            const result = await html5QrCode.scanFileV2(file, true);
            if (result && typeof result.decodedText === "string" && result.decodedText.trim().length > 0) {
                return result.decodedText;
            }
        }

        return html5QrCode.scanFile(file, true);
    };

    document.querySelectorAll("[data-qr-scanner]").forEach((scanner, index) => {
        const appTechQrPattern = /^appTech-[A-Za-z0-9]{9}$/i;
        const invalidQrMessage = "Mã QR không hợp lệ. Chỉ chấp nhận mã QR do hệ thống sinh theo dạng appTech-XXXXXXXXX.";
        const input = scanner.querySelector("[data-qr-result]");
        const startButton = scanner.querySelector("[data-qr-start]");
        const stopButton = scanner.querySelector("[data-qr-stop]");
        const clearButton = scanner.querySelector("[data-qr-clear]");
        const fileInput = scanner.querySelector("[data-qr-file]");
        const panel = scanner.querySelector("[data-qr-panel]");
        const reader = scanner.querySelector("[data-qr-reader]");
        const status = scanner.querySelector("[data-qr-status]");
        const summary = scanner.querySelector("[data-qr-summary]");
        const summaryDefault = scanner.querySelector("[data-qr-summary-default]");
        const summaryIcon = scanner.querySelector("[data-qr-summary-icon]");
        const summaryRender = scanner.querySelector("[data-qr-render]");
        const resultShell = scanner.querySelector("[data-qr-result-shell]");
        const resultDisplay = scanner.querySelector("[data-qr-result-display]");
        const cameraShell = scanner.querySelector("[data-qr-camera-shell]");
        const cameraSelect = scanner.querySelector("[data-qr-camera]");
        const form = scanner.closest("form");
        const vatTuIdInput = form?.querySelector("input[name='Form.Id']");

        if (!(input instanceof HTMLInputElement) ||
            !(startButton instanceof HTMLButtonElement) ||
            !(stopButton instanceof HTMLButtonElement) ||
            !(clearButton instanceof HTMLButtonElement) ||
            !(fileInput instanceof HTMLInputElement) ||
            !(panel instanceof HTMLElement) ||
            !(reader instanceof HTMLElement) ||
            !(status instanceof HTMLElement) ||
            !(cameraShell instanceof HTMLElement) ||
            !(cameraSelect instanceof HTMLSelectElement)) {
            return;
        }

        if (!reader.id) {
            reader.id = `qr-reader-${index + 1}`;
        }

        let html5QrCode = null;
        let isRunning = false;
        let isFileProcessing = false;
        let isStarting = false;
        let cameraOptions = [];
        let selectedCameraId = "";
        let fileScanToken = 0;
        let isLookupProcessing = false;
        const recentQrValues = new Map();
        let scannerMode = "idle";
        let renderToken = 0;

        let statusShakeTimeoutId = 0;

        const updateStatus = (message, tone = "default") => {
            status.textContent = message;
            const isError = tone === "error";
            status.classList.toggle("is-error", isError);

            if (statusShakeTimeoutId) {
                window.clearTimeout(statusShakeTimeoutId);
                statusShakeTimeoutId = 0;
            }

            if (isError) {
                status.classList.remove("is-shaking");
                void status.offsetWidth;
                status.classList.add("is-shaking");
                statusShakeTimeoutId = window.setTimeout(() => {
                    status.classList.remove("is-shaking");
                    statusShakeTimeoutId = 0;
                }, 450);
                return;
            }

            status.classList.remove("is-shaking");
        };

        const isValidAppTechQr = (value) => appTechQrPattern.test(`${value || ""}`.trim());

        const setQrValue = (value) => {
            input.value = `${value || ""}`.trim();
            updateSummary();
        };

        const getCurrentVatTuId = () => {
            if (!(vatTuIdInput instanceof HTMLInputElement)) {
                return "";
            }

            return `${vatTuIdInput.value || ""}`.trim();
        };

        const isQrCodeInUse = async (qrValue) => {
            const params = new URLSearchParams({ value: qrValue });
            const currentVatTuId = getCurrentVatTuId();
            if (currentVatTuId) {
                params.set("excludingId", currentVatTuId);
            }

            const response = await fetch(`/VatTu/ValidateQrCodeUsage?${params.toString()}`, {
                method: "GET",
                headers: {
                    "X-Requested-With": "XMLHttpRequest"
                },
                cache: "no-store"
            });

            if (!response.ok) {
                return false;
            }

            const payload = await response.json();
            return Boolean(payload?.isInUse);
        };

        const tryAcceptQrValue = async (qrValue, successMessageBuilder) => {
            const normalizedValue = `${qrValue || ""}`.trim();
            if (!isValidAppTechQr(normalizedValue)) {
                setQrValue("");
                updateStatus(invalidQrMessage);
                return false;
            }

            try {
                if (await isQrCodeInUse(normalizedValue)) {
                    setQrValue("");
                    updateStatus("Mã QR đang được sử dụng trên hệ thống. Vui lòng dùng mã khác.");
                    return false;
                }
            } catch {
                updateStatus("Không kiểm tra được trạng thái mã QR trên hệ thống. Hãy thử lại.");
                return false;
            }

            setQrValue(normalizedValue);
            updateStatus(successMessageBuilder(normalizedValue));
            return true;
        };

        const clearRenderedQr = () => {
            renderToken += 1;
            if (summaryRender instanceof HTMLElement) {
                summaryRender.replaceChildren();
                summaryRender.classList.add("is-hidden");
            }
        };

        const renderQrPreview = async (qrValue) => {
            if (!(summaryRender instanceof HTMLElement) || !qrValue) {
                clearRenderedQr();
                return;
            }

            const currentRenderToken = ++renderToken;

            try {
                const response = await fetch(`/VatTu/QrPreview?value=${encodeURIComponent(qrValue)}`, {
                    method: "GET",
                    headers: {
                        "X-Requested-With": "XMLHttpRequest"
                    },
                    cache: "no-store"
                });

                if (!response.ok) {
                    clearRenderedQr();
                    return;
                }

                const svgMarkup = (await response.text()).trim();
                if (currentRenderToken !== renderToken || !svgMarkup) {
                    return;
                }

                summaryRender.innerHTML = svgMarkup;
                const svg = summaryRender.querySelector("svg");
                if (!svg) {
                    clearRenderedQr();
                    return;
                }

                summaryRender.classList.remove("is-hidden");
            } catch (error) {
                console.error("QR preview render failed.", error);
                if (currentRenderToken === renderToken) {
                    clearRenderedQr();
                }
            }
        };

        const setScannerMode = (mode) => {
            scannerMode = mode;
            const isCameraMode = mode === "camera";
            const isBusy = mode === "camera" || mode === "file";

            panel.hidden = !isCameraMode;
            stopButton.hidden = !isBusy;
            updateSummary();
        };

        const toFriendlyErrorMessage = (error) => {
            const message = typeof error === "string"
                ? error
                : error instanceof Error
                    ? error.message
                    : "";
            const normalized = message.toLowerCase();

            if (normalized.includes("permission") || normalized.includes("notallowederror")) {
                return "Trình duyệt chưa được cấp quyền camera. Hãy cho phép camera rồi thử lại.";
            }

            if (normalized.includes("notfounderror") || normalized.includes("device not found")) {
                return "Không tìm thấy camera phù hợp trên thiết bị này.";
            }

            if (normalized.includes("notreadableerror") || normalized.includes("trackstarterror")) {
                return "Camera đang được ứng dụng khác sử dụng. Hãy đóng ứng dụng đó rồi thử lại.";
            }

            if (normalized.includes("secure") || normalized.includes("https")) {
                return "Camera chỉ hoạt động trên HTTPS hoặc localhost.";
            }

            return "Không thể truy cập camera hoặc khởi động bộ quét QR.";
        };

        const updateSummary = () => {
            const qrValue = input.value.trim();
            if (!(summary instanceof HTMLElement) ||
                !(summaryIcon instanceof HTMLElement)) {
                return;
            }

            const hasCode = qrValue.length > 0;
            const isCameraMode = scannerMode === "camera";

            summary.classList.toggle("is-empty", !hasCode && !isCameraMode);
            summaryIcon.classList.toggle("fa-expand", !hasCode && !isCameraMode);
            summaryDefault?.classList.toggle("is-hidden", hasCode || isCameraMode);
            resultShell?.classList.toggle("is-hidden", !hasCode);

            if (resultDisplay instanceof HTMLElement) {
                resultDisplay.textContent = qrValue;
            }

            if (isCameraMode || !hasCode) {
                clearRenderedQr();
                return;
            }

            void renderQrPreview(qrValue);
        };

        const populateCameraOptions = (cameras) => {
            cameraOptions = cameras;
            cameraSelect.innerHTML = "";

            cameraOptions.forEach((camera) => {
                const option = document.createElement("option");
                option.value = camera.id;
                option.textContent = camera.label || `Camera ${cameraSelect.options.length + 1}`;
                cameraSelect.appendChild(option);
            });

            cameraShell.hidden = cameraOptions.length <= 1;
            if (!selectedCameraId || !cameraOptions.some((camera) => camera.id === selectedCameraId)) {
                const preferredCamera = getPreferredQrCamera(cameraOptions);
                selectedCameraId = preferredCamera?.id || "";
                appendQrDebugLog("qr_preferred_camera", {
                    scope: "vat-tu-detail",
                    selectedCameraId,
                    label: getQrCameraLabel(selectedCameraId, cameraOptions)
                });
            }

            if (selectedCameraId) {
                cameraSelect.value = selectedCameraId;
                syncLiveSelectState(cameraSelect);
            }
        };

        const ensureCamerasLoaded = async () => {
            if (cameraOptions.length > 0) {
                return cameraOptions;
            }

            const cameras = typeof window.Html5Qrcode.getCameras === "function"
                ? await window.Html5Qrcode.getCameras()
                : [];
            appendQrDebugLog("qr_cameras_loaded", {
                scope: "vat-tu-detail",
                cameras: summarizeQrCameras(cameras)
            });
            populateCameraOptions(cameras);
            return cameraOptions;
        };

        const stopScanner = async (message = "Đã dừng quét camera.") => {
            if (html5QrCode && isRunning) {
                try {
                    await html5QrCode.stop();
                } catch {
                }
            }

            isRunning = false;
            isFileProcessing = false;
            setScannerMode("idle");
            updateStatus(message);
        };

        startButton.addEventListener("click", async () => {
            appendQrDebugLog("qr_click_start", {
                scope: "vat-tu-detail",
                isRunning,
                isStarting,
                isFileProcessing,
                state: getQrScannerStateName(html5QrCode)
            });
            if (isStarting) {
                updateStatus("Camera đang quét QR.");
                return;
            }

            if (isRunning) {
                appendQrDebugLog("qr_force_restart", {
                    scope: "global-lookup",
                    state: getQrScannerStateName(html5QrCode)
                });
                updateStatus("Đang khởi động lại camera.");
                await stopScanner("");
                await resetQrScannerInstance(html5QrCode, "global-lookup-force-restart");
                html5QrCode = null;
                isRunning = false;
            }

            isStarting = true;
            if (!window.isSecureContext && window.location.hostname !== "localhost" && window.location.hostname !== "127.0.0.1") {
                updateStatus("Trình duyệt chỉ cho phép mở camera trên HTTPS hoặc localhost.");
                isStarting = false;
                return;
            }

            if (!navigator.mediaDevices?.getUserMedia) {
                updateStatus("Thiết bị hiện tại không hỗ trợ camera trên trình duyệt này.");
                isStarting = false;
                return;
            }

            try {
                await loadHtml5Qrcode();
            } catch {
                updateStatus("Không tải được thư viện quét QR. Bạn vẫn có thể nhập tay mã QR.");
                isStarting = false;
                return;
            }

            if (!html5QrCode) {
                html5QrCode = createHtml5QrCodeInstance(reader.id);
            }

            try {
                await resetQrScannerInstance(html5QrCode, "vat-tu-detail");
                const cameras = await ensureCamerasLoaded();

                if (!selectedCameraId && cameras.length > 0) {
                    selectedCameraId = cameras[0].id;
                }

                if (!selectedCameraId) {
                    updateStatus("Không tìm thấy camera để quét QR.");
                    isStarting = false;
                    return;
                }

                setScannerMode("camera");
                updateStatus("Đưa mã QR vào giữa khung quét.");

                await startQrScannerCamera(
                    html5QrCode,
                    reader,
                    selectedCameraId,
                    async (decodedText) => {
                        const accepted = await tryAcceptQrValue(
                            decodedText,
                            (normalizedValue) => `Đã nhận mã QR: ${normalizedValue}`);
                        if (!accepted) {
                            await stopScanner(status.textContent || invalidQrMessage);
                            return;
                        }

                        await stopScanner(`Đã nhận mã QR: ${input.value}`);
                    },
                    () => {
                    });

                isRunning = true;
            } catch (error) {
                console.error("QR scanner start failed.", error);
                isRunning = false;
                setScannerMode("idle");
                updateStatus(toFriendlyErrorMessage(error));
            } finally {
                isStarting = false;
            }
        });

        stopButton.addEventListener("click", async () => {
            if (isFileProcessing) {
                fileScanToken += 1;
                isFileProcessing = false;
                setScannerMode("idle");
                updateStatus("Đã dừng xử lý ảnh QR.");
                return;
            }

            await stopScanner();
        });

        clearButton.addEventListener("click", () => {
            setQrValue("");
            updateStatus("Đã xóa mã QR hiện tại.");
        });

        fileInput.addEventListener("change", async () => {
            const [file] = fileInput.files || [];
            if (!file) {
                return;
            }

            try {
                await loadHtml5Qrcode();
            } catch {
                updateStatus("Không tải được thư viện quét QR để đọc từ ảnh.");
                fileInput.value = "";
                return;
            }

            if (!html5QrCode) {
                html5QrCode = createHtml5QrCodeInstance(reader.id);
            }

            try {
                if (isRunning) {
                    await stopScanner("");
                }

                isFileProcessing = true;
                const currentFileToken = ++fileScanToken;
                setScannerMode("file");
                updateStatus("Đang đọc mã QR từ ảnh đã chọn...");

                const decodedText = await scanQrFromImageFile(html5QrCode, file);
                if (!isFileProcessing || currentFileToken !== fileScanToken) {
                    return;
                }

                isFileProcessing = false;
                setScannerMode("idle");
                await tryAcceptQrValue(
                    decodedText,
                    (normalizedValue) => `Đã nhận mã QR từ ảnh: ${normalizedValue}`);
            } catch {
                isFileProcessing = false;
                setScannerMode("idle");
                updateStatus("Không đọc được QR từ ảnh đã chọn. Hãy thử ảnh rõ hơn hoặc ảnh khác.");
            } finally {
                fileInput.value = "";
            }
        });

        cameraSelect.addEventListener("change", async () => {
            selectedCameraId = cameraSelect.value;
            appendQrDebugLog("qr_camera_select_change", {
                scope: "vat-tu-detail",
                selectedCameraId,
                label: getQrCameraLabel(selectedCameraId, cameraOptions)
            });
            if (!isRunning || !selectedCameraId) {
                return;
            }

            await stopScanner("Đang chuyển camera...");
            startButton.click();
        });

        input.addEventListener("input", () => {
            updateSummary();
        });

        window.addEventListener("pagehide", () => {
            void stopScanner("");
        });

        setScannerMode("idle");
        updateSummary();
    });

    (() => {
        const trigger = document.querySelector("[data-qr-lookup-trigger]");
        const popupShell = document.querySelector("[data-qr-lookup-popup-shell]");
        const popup = document.getElementById("qrLookupPopup");
        const closeButtons = popupShell ? Array.from(popupShell.querySelectorAll("[data-qr-lookup-close]")) : [];
        const startButton = popupShell?.querySelector("[data-global-qr-start]");
        const stopButton = popupShell?.querySelector("[data-global-qr-stop]");
        const fileInput = popupShell?.querySelector("[data-global-qr-file]");
        const panel = popupShell?.querySelector("[data-global-qr-panel]");
        const reader = popupShell?.querySelector("[data-global-qr-reader]");
        const defaultDisplay = popupShell?.querySelector("[data-global-qr-default]");
        const status = popupShell?.querySelector("[data-global-qr-status]");
        const input = popupShell?.querySelector("[data-global-qr-result]");
        const cameraShell = popupShell?.querySelector("[data-global-qr-camera-shell]");
        const cameraSelect = popupShell?.querySelector("[data-global-qr-camera]");

        if (!(trigger instanceof HTMLButtonElement) ||
            !(popupShell instanceof HTMLElement) ||
            !(popup instanceof HTMLElement) ||
            !(startButton instanceof HTMLButtonElement) ||
            !(stopButton instanceof HTMLButtonElement) ||
            !(fileInput instanceof HTMLInputElement) ||
            !(panel instanceof HTMLElement) ||
            !(reader instanceof HTMLElement) ||
            !(defaultDisplay instanceof HTMLElement) ||
            !(status instanceof HTMLElement) ||
            !(input instanceof HTMLInputElement) ||
            !(cameraShell instanceof HTMLElement) ||
            !(cameraSelect instanceof HTMLSelectElement)) {
            return;
        }

        const appTechQrPattern = /^appTech-[A-Za-z0-9]{9}$/i;
        let html5QrCode = null;
        let isRunning = false;
        let isFileProcessing = false;
        let isStarting = false;
        let cameraOptions = [];
        let selectedCameraId = "";
        let fileScanToken = 0;
        let scannerMode = "idle";

        if (!reader.id) {
            reader.id = "global-qr-reader";
        }

        let statusShakeTimeoutId = 0;

        const updateStatus = (message, tone = "default") => {
            status.textContent = message;
            const isError = tone === "error";
            status.classList.toggle("is-error", isError);

            if (statusShakeTimeoutId) {
                window.clearTimeout(statusShakeTimeoutId);
                statusShakeTimeoutId = 0;
            }

            if (isError) {
                status.classList.remove("is-shaking");
                void status.offsetWidth;
                status.classList.add("is-shaking");
                statusShakeTimeoutId = window.setTimeout(() => {
                    status.classList.remove("is-shaking");
                    statusShakeTimeoutId = 0;
                }, 450);
                return;
            }

            status.classList.remove("is-shaking");
        };

        const setScannerMode = (mode) => {
            scannerMode = mode;
            const isBusy = mode === "camera" || mode === "file";
            panel.hidden = mode !== "camera";
            defaultDisplay.classList.toggle("is-hidden", mode === "camera");
            stopButton.hidden = !isBusy;
        };

        const setResult = (value) => {
            input.value = `${value || ""}`.trim();
        };

        const resetLookupState = () => {
            setResult("");
            fileInput.value = "";
            updateStatus("Sẵn sàng quét QR để tra cứu vật tư trên hệ thống.");
            setScannerMode("idle");
        };

        const populateCameraOptions = (cameras) => {
            cameraOptions = cameras;
            cameraSelect.innerHTML = "";

            cameraOptions.forEach((camera) => {
                const option = document.createElement("option");
                option.value = camera.id;
                option.textContent = camera.label || `Camera ${cameraSelect.options.length + 1}`;
                cameraSelect.appendChild(option);
            });

            cameraShell.hidden = cameraOptions.length <= 1;
            if (!selectedCameraId || !cameraOptions.some((camera) => camera.id === selectedCameraId)) {
                const preferredCamera = getPreferredQrCamera(cameraOptions);
                selectedCameraId = preferredCamera?.id || "";
                appendQrDebugLog("qr_preferred_camera", {
                    scope: "global-lookup",
                    selectedCameraId,
                    label: getQrCameraLabel(selectedCameraId, cameraOptions)
                });
            }

            if (selectedCameraId) {
                cameraSelect.value = selectedCameraId;
                syncLiveSelectState(cameraSelect);
            }
        };

        const ensureCamerasLoaded = async () => {
            if (cameraOptions.length > 0) {
                return cameraOptions;
            }

            const cameras = typeof window.Html5Qrcode.getCameras === "function"
                ? await window.Html5Qrcode.getCameras()
                : [];
            appendQrDebugLog("qr_cameras_loaded", {
                scope: "global-lookup",
                cameras: summarizeQrCameras(cameras)
            });
            populateCameraOptions(cameras);
            return cameraOptions;
        };

        const stopScanner = async (message = "Đã dừng quét QR.") => {
            if (html5QrCode && isRunning) {
                try {
                    await html5QrCode.stop();
                } catch {
                }
            }

            isRunning = false;
            isFileProcessing = false;
            setScannerMode("idle");
            if (message) {
                updateStatus(message);
            }
        };

        const closePopup = async () => {
            await stopScanner("");
            popupShell.hidden = true;
            trigger.setAttribute("aria-expanded", "false");
            resetLookupState();
        };

        const openPopup = () => {
            resetLookupState();
            popupShell.hidden = false;
            trigger.setAttribute("aria-expanded", "true");
            popup.style.left = "50%";
            popup.style.top = "50%";
            popup.style.transform = "translate(-50%, -50%)";
        };

        const lookupQrAndRedirect = async (qrValue) => {
            const normalizedValue = `${qrValue || ""}`.trim();
            if (!appTechQrPattern.test(normalizedValue)) {
                setResult("");
                updateStatus("Mã QR không hợp lệ. Chỉ chấp nhận mã QR do hệ thống sinh theo dạng appTech-XXXXXXXXX.", "error");
                return false;
            }

            setResult(normalizedValue);
            updateStatus("Đang kiểm tra mã QR trên hệ thống...");

            try {
                const response = await fetch(`/VatTu/FindByQrCode?value=${encodeURIComponent(normalizedValue)}`, {
                    method: "GET",
                    headers: {
                        "X-Requested-With": "XMLHttpRequest"
                    },
                    cache: "no-store"
                });

                if (!response.ok) {
                    updateStatus("Không kiểm tra được mã QR trên hệ thống. Hãy thử lại.", "error");
                    return false;
                }

                const payload = await response.json();
                if (!payload?.found || !payload?.redirectUrl) {
                    updateStatus("Không tìm thấy vật tư cho mã QR này.", "error");
                    return false;
                }

                updateStatus("Đang mở thông tin vật tư...");
                window.location.assign(payload.redirectUrl);
                return true;
            } catch {
                updateStatus("Không kiểm tra được mã QR trên hệ thống. Hãy thử lại.", "error");
                return false;
            }
        };

        trigger.addEventListener("click", () => {
            if (popupShell.hidden) {
                openPopup();
                return;
            }

            void closePopup();
        });

        closeButtons.forEach((button) => {
            button.addEventListener("click", () => {
                void closePopup();
            });
        });

        document.addEventListener("keydown", (event) => {
            if (event.key === "Escape" && !popupShell.hidden) {
                void closePopup();
            }
        });

        startButton.addEventListener("click", async () => {
            appendQrDebugLog("qr_click_start", {
                scope: "global-lookup",
                isRunning,
                isStarting,
                isFileProcessing,
                state: getQrScannerStateName(html5QrCode)
            });
            if (isStarting) {
                updateStatus("Camera đang quét QR.");
                return;
            }

            if (isRunning) {
                appendQrDebugLog("qr_force_restart", {
                    scope: "global-lookup",
                    state: getQrScannerStateName(html5QrCode)
                });
                updateStatus("Đang khởi động lại camera.");
                await stopScanner("");
                await resetQrScannerInstance(html5QrCode, "global-lookup-force-restart");
                html5QrCode = null;
                isRunning = false;
            }

            isStarting = true;
            if (!window.isSecureContext && window.location.hostname !== "localhost" && window.location.hostname !== "127.0.0.1") {
                updateStatus("Trình duyệt chỉ cho phép mở camera trên HTTPS hoặc localhost.");
                isStarting = false;
                return;
            }

            if (!navigator.mediaDevices?.getUserMedia) {
                updateStatus("Thiết bị hiện tại không hỗ trợ camera trên trình duyệt này.");
                isStarting = false;
                return;
            }

            try {
                await loadHtml5Qrcode();
            } catch {
                updateStatus("Không tải được thư viện quét QR để tra cứu.");
                isStarting = false;
                return;
            }

            if (!html5QrCode) {
                html5QrCode = createHtml5QrCodeInstance(reader.id);
            }

            try {
                await resetQrScannerInstance(html5QrCode, "global-lookup");
                const cameras = await ensureCamerasLoaded();
                if (!selectedCameraId && cameras.length > 0) {
                    selectedCameraId = cameras[0].id;
                }

                if (!selectedCameraId) {
                    updateStatus("Không tìm thấy camera để quét QR.");
                    isStarting = false;
                    return;
                }

                setScannerMode("camera");
                updateStatus("Đưa mã QR vật tư vào giữa khung quét.");

                await startQrScannerCamera(
                    html5QrCode,
                    reader,
                    selectedCameraId,
                    async (decodedText) => {
                        const matched = await lookupQrAndRedirect(decodedText);
                        await stopScanner(matched ? "Đang mở thông tin vật tư..." : "");
                    },
                    () => {
                    });

                isRunning = true;
            } catch (error) {
                console.error("Global QR scanner start failed.", error);
                isRunning = false;
                setScannerMode("idle");
                updateStatus("Không thể truy cập camera hoặc khởi động bộ quét QR.");
            } finally {
                isStarting = false;
            }
        });

        stopButton.addEventListener("click", async () => {
            if (isFileProcessing) {
                fileScanToken += 1;
                isFileProcessing = false;
                setScannerMode("idle");
                updateStatus("Đã dừng xử lý ảnh QR.");
                return;
            }

            await stopScanner();
        });

        fileInput.addEventListener("change", async () => {
            const [file] = fileInput.files || [];
            if (!file) {
                return;
            }

            try {
                await loadHtml5Qrcode();
            } catch {
                updateStatus("Không tải được thư viện quét QR để đọc từ ảnh.");
                fileInput.value = "";
                return;
            }

            if (!html5QrCode) {
                html5QrCode = createHtml5QrCodeInstance(reader.id);
            }

            try {
                if (isRunning) {
                    await stopScanner("");
                }

                isFileProcessing = true;
                const currentFileToken = ++fileScanToken;
                setScannerMode("file");
                updateStatus("Đang đọc mã QR từ ảnh đã chọn...");

                const decodedText = await scanQrFromImageFile(html5QrCode, file);
                if (!isFileProcessing || currentFileToken !== fileScanToken) {
                    return;
                }

                isFileProcessing = false;
                setScannerMode("idle");
                await lookupQrAndRedirect(decodedText);
            } catch {
                isFileProcessing = false;
                setScannerMode("idle");
                updateStatus("Không đọc được QR từ ảnh đã chọn. Hãy thử ảnh rõ hơn hoặc ảnh khác.");
            } finally {
                fileInput.value = "";
            }
        });

                cameraSelect.addEventListener("change", async () => {
                    if (isStarting) {
                        appendQrDebugLog("qr_camera_select_ignored_while_starting", {
                            scope: "global-lookup",
                            value: cameraSelect.value
                        });
                        cameraSelect.value = selectedCameraId;
                        syncLiveSelectState(cameraSelect);
                        return;
                    }

                    selectedCameraId = cameraSelect.value;
                    appendQrDebugLog("qr_camera_select_change", {
                        scope: "global-lookup",
                selectedCameraId,
                label: getQrCameraLabel(selectedCameraId, cameraOptions)
            });
            if (!isRunning || !selectedCameraId) {
                return;
            }

            await stopScanner("Đang chuyển camera...");
            startButton.click();
        });
    })();

    (() => {
        const openButton = document.querySelector("[data-qr-assignment-open]");
        const popupShell = document.querySelector("[data-qr-assignment-shell]");
        const popup = document.getElementById("qrAssignmentModal");
        const root = popupShell?.querySelector("[data-qr-assignment-root]");
        const closeButtons = popupShell ? Array.from(popupShell.querySelectorAll("[data-qr-assignment-close]")) : [];
        const searchForm = popupShell?.querySelector("[data-qr-assignment-search-form]");
        const searchButton = popupShell?.querySelector("[data-qr-assignment-search-button]");
        const resetButton = popupShell?.querySelector("[data-qr-assignment-reset]");
        const headerAlert = popupShell?.querySelector("[data-qr-assignment-alert]");
        const headerProgressNode = popupShell?.querySelector("[data-qr-assignment-header-progress]");
        const countNode = popupShell?.querySelector("[data-qr-assignment-count]");
        const hintNode = popupShell?.querySelector("[data-qr-assignment-hint]");
        const resultsNode = popupShell?.querySelector("[data-qr-assignment-results]");
        const activeCard = popupShell?.querySelector("[data-qr-assignment-active-card]");
        const activeTitleNode = popupShell?.querySelector("[data-qr-assignment-active-title]");
        const activeMetaNode = popupShell?.querySelector("[data-qr-assignment-active-meta]");
        const input = popupShell?.querySelector("[data-qr-assignment-result]");
        const resultShell = popupShell?.querySelector("[data-qr-assignment-result-shell]");
        const resultDisplay = popupShell?.querySelector("[data-qr-assignment-result-display]");
        const startButton = popupShell?.querySelector("[data-qr-assignment-start]");
        const stopButton = popupShell?.querySelector("[data-qr-assignment-stop]");
        const fileInput = popupShell?.querySelector("[data-qr-assignment-file]");
        const panel = popupShell?.querySelector("[data-qr-assignment-panel]");
        const reader = popupShell?.querySelector("[data-qr-assignment-reader]");
        const defaultDisplay = popupShell?.querySelector("[data-qr-assignment-default]");
        const status = popupShell?.querySelector("[data-qr-assignment-status]");
        const cameraShell = popupShell?.querySelector("[data-qr-assignment-camera-shell]");
        const cameraSelect = popupShell?.querySelector("[data-qr-assignment-camera]");

        if (!(openButton instanceof HTMLButtonElement) ||
            !(popupShell instanceof HTMLElement) ||
            !(popup instanceof HTMLElement) ||
            !(root instanceof HTMLElement) ||
            !(searchForm instanceof HTMLFormElement) ||
            !(searchButton instanceof HTMLButtonElement) ||
            !(resetButton instanceof HTMLButtonElement) ||
            !(headerProgressNode instanceof HTMLElement) ||
            !(countNode instanceof HTMLElement) ||
            !(hintNode instanceof HTMLElement) ||
            !(resultsNode instanceof HTMLElement) ||
            !(activeCard instanceof HTMLElement) ||
            !(activeTitleNode instanceof HTMLElement) ||
            !(activeMetaNode instanceof HTMLElement) ||
            !(input instanceof HTMLInputElement) ||
            !(resultShell instanceof HTMLElement) ||
            !(resultDisplay instanceof HTMLElement) ||
            !(startButton instanceof HTMLButtonElement) ||
            !(stopButton instanceof HTMLButtonElement) ||
            !(fileInput instanceof HTMLInputElement) ||
            !(panel instanceof HTMLElement) ||
            !(reader instanceof HTMLElement) ||
            !(defaultDisplay instanceof HTMLElement) ||
            !(status instanceof HTMLElement) ||
            !(cameraShell instanceof HTMLElement) ||
            !(cameraSelect instanceof HTMLSelectElement)) {
            return;
        }

        const searchUrl = root.dataset.searchUrl || "";
        const assignUrl = root.dataset.assignUrl || "";
        const antiForgeryToken = searchForm.querySelector("input[name='__RequestVerificationToken']");
        const appTechQrPattern = /^appTech-[A-Za-z0-9]{9}$/i;
        const invalidQrMessage = "Mã QR không hợp lệ. Chỉ chấp nhận mã QR do hệ thống sinh theo dạng appTech-XXXXXXXXX.";
        const defaultHint = "Danh sách sẽ hiển thị sau khi tìm kiếm.";
        const defaultStatus = "Tìm danh sách vật tư trước, sau đó quét QR bằng camera hoặc ảnh.";

        if (!reader.id) {
            reader.id = "qr-assignment-reader";
        }

        let html5QrCode = null;
        let isRunning = false;
        let isFileProcessing = false;
        let isSearchPending = false;
        let isAssigning = false;
        let isStarting = false;
        let cameraOptions = [];
        let selectedCameraId = "";
        let fileScanToken = 0;
        let scannerMode = "idle";
        let activeItemId = 0;
        let hasLoadedResults = false;
        let items = [];
        let totalLoadedItems = 0;
        let lastAssignedQr = "";
        let lastAssignedAt = 0;
        let statusShakeTimeoutId = 0;

        const escapeHtml = (value) => `${value || ""}`
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll("\"", "&quot;")
            .replaceAll("'", "&#39;");

        const normalizeQrValue = (value) => `${value || ""}`.trim();
        const isExistingQrMessage = (message) => {
            const normalized = `${message || ""}`.toLowerCase();
            return normalized.includes("tồn tại") ||
                normalized.includes("đã được sử dụng") ||
                normalized.includes("đang được sử dụng") ||
                normalized.includes("exists") ||
                normalized.includes("already");
        };

        const setHeaderAlert = (message, shouldShake = false) => {
            if (!(headerAlert instanceof HTMLElement)) {
                return;
            }

            const normalizedMessage = `${message || ""}`.trim();
            headerAlert.textContent = normalizedMessage;
            headerAlert.hidden = normalizedMessage.length === 0;

            if (!normalizedMessage) {
                headerAlert.classList.remove("is-shaking");
                return;
            }

            if (shouldShake) {
                headerAlert.classList.remove("is-shaking");
                void headerAlert.offsetWidth;
                headerAlert.classList.add("is-shaking");
            }
        };

        const setResult = (value) => {
            input.value = normalizeQrValue(value);
            resultDisplay.textContent = input.value;
            resultShell.classList.toggle("is-hidden", input.value.length === 0);
        };

        const updateStatus = (message, tone = "default") => {
            status.textContent = message;
            const isError = tone === "error";
            status.classList.toggle("is-error", isError);

            if (isError && isExistingQrMessage(message)) {
                setHeaderAlert(message, true);
            } else if (!isError) {
                setHeaderAlert("");
            }

            if (statusShakeTimeoutId) {
                window.clearTimeout(statusShakeTimeoutId);
                statusShakeTimeoutId = 0;
            }

            if (isError) {
                status.classList.remove("is-shaking");
                void status.offsetWidth;
                status.classList.add("is-shaking");
                statusShakeTimeoutId = window.setTimeout(() => {
                    status.classList.remove("is-shaking");
                    statusShakeTimeoutId = 0;
                }, 450);
                return;
            }

            status.classList.remove("is-shaking");
        };

        const setScannerMode = (mode) => {
            scannerMode = mode;
            const isBusy = mode === "camera" || mode === "file";
            panel.hidden = mode !== "camera";
            defaultDisplay.classList.toggle("is-hidden", mode === "camera");
            stopButton.hidden = !isBusy;
        };

        const populateCameraOptions = (cameras) => {
            cameraOptions = cameras;
            cameraSelect.innerHTML = "";

            cameraOptions.forEach((camera) => {
                const option = document.createElement("option");
                option.value = camera.id;
                option.textContent = camera.label || `Camera ${cameraSelect.options.length + 1}`;
                cameraSelect.appendChild(option);
            });

            cameraShell.hidden = cameraOptions.length <= 1;
            if (!selectedCameraId || !cameraOptions.some((camera) => camera.id === selectedCameraId)) {
                const preferredCamera = getPreferredQrCamera(cameraOptions);
                selectedCameraId = preferredCamera?.id || "";
                appendQrDebugLog("qr_preferred_camera", {
                    scope: "qr-assignment",
                    selectedCameraId,
                    label: getQrCameraLabel(selectedCameraId, cameraOptions)
                });
            }

            if (selectedCameraId) {
                cameraSelect.value = selectedCameraId;
                syncLiveSelectState(cameraSelect);
            }
        };

        const ensureCamerasLoaded = async () => {
            if (cameraOptions.length > 0) {
                return cameraOptions;
            }

            const cameras = typeof window.Html5Qrcode.getCameras === "function"
                ? await window.Html5Qrcode.getCameras()
                : [];
            appendQrDebugLog("qr_cameras_loaded", {
                scope: "qr-assignment",
                cameras: summarizeQrCameras(cameras)
            });
            populateCameraOptions(cameras);
            return cameraOptions;
        };

        const stopScanner = async (message = "Đã dừng quét QR.") => {
            if (html5QrCode && isRunning) {
                try {
                    await html5QrCode.stop();
                } catch {
                }
            }

            isRunning = false;
            isFileProcessing = false;
            setScannerMode("idle");
            if (message) {
                updateStatus(message);
            }
        };

        const getActiveItem = () => items.find((item) => item.id === activeItemId) || null;

        const buildItemMeta = (item) => {
            const segments = [];
            if (item.tenHangHoa) {
                segments.push(item.tenHangHoa);
            }
            if (item.maPhieuNhap) {
                segments.push(`Phiếu nhập: ${item.maPhieuNhap}`);
            }
            if (item.tenKho) {
                segments.push(`Kho: ${item.tenKho}`);
            }
            if (item.viTriLuuKho) {
                segments.push(`Vị trí: ${item.viTriLuuKho}`);
            }
            if (item.maSoLo) {
                segments.push(`Lô: ${item.maSoLo}`);
            }
            if (item.qrCode) {
                segments.push(`QR hiện tại: ${item.qrCode}`);
            } else {
                segments.push("Chưa có QR");
            }

            return segments.join(" • ");
        };

        const syncSummary = () => {
            countNode.textContent = `${items.length}`;
            headerProgressNode.textContent = `Còn ${items.length} / ${totalLoadedItems} vật tư`;

            if (!hasLoadedResults) {
                hintNode.textContent = defaultHint;
                activeCard.classList.remove("is-success");
                activeTitleNode.textContent = "Chưa có vật tư mục tiêu";
                activeMetaNode.textContent = "Tìm kiếm để nạp danh sách vật tư cần đánh mã QR.";
                headerProgressNode.textContent = "Còn 0 / 0 vật tư";
                return;
            }

            if (items.length === 0) {
                hintNode.textContent = "Đã hết vật tư trong danh sách.";
                activeCard.classList.add("is-success");
                activeTitleNode.textContent = "Hoàn thành đánh mã QR";
                activeMetaNode.textContent = "Đã hết vật tư trong danh sách cần đánh mã QR.";
                return;
            }

            const activeItem = getActiveItem();
            hintNode.textContent = "Quét QR hợp lệ để gán cho vật tư đang chọn.";
            activeCard.classList.remove("is-success");
            if (!activeItem) {
                activeTitleNode.textContent = "Chưa chọn vật tư mục tiêu";
                activeMetaNode.textContent = "Chọn một vật tư trong danh sách bên dưới để bắt đầu.";
                return;
            }

            activeTitleNode.textContent = activeItem.tenChiTiet || `Vật tư #${activeItem.id}`;
            activeMetaNode.textContent = buildItemMeta(activeItem);
        };

        const renderResults = () => {
            resultsNode.innerHTML = "";

            if (!hasLoadedResults) {
                resultsNode.innerHTML = '<div class="master-data-empty qr-assignment-empty">Chưa có danh sách vật tư để đánh mã QR.</div>';
                syncSummary();
                return;
            }

            if (items.length === 0) {
                resultsNode.innerHTML = '<div class="master-data-empty qr-assignment-empty">Không còn vật tư nào trong danh sách chờ.</div>';
                syncSummary();
                return;
            }

            const fragment = document.createDocumentFragment();
            items.forEach((item, index) => {
                const button = document.createElement("button");
                button.type = "button";
                button.className = `qr-assignment-result-item${item.id === activeItemId ? " is-active" : ""}`;
                button.dataset.qrAssignmentTargetId = `${item.id}`;
                button.setAttribute("aria-pressed", String(item.id === activeItemId));
                button.innerHTML = `
                    <span class="qr-assignment-result-order">${index + 1}</span>
                    <span class="qr-assignment-result-body">
                        <strong>${escapeHtml(item.tenChiTiet || `Vật tư #${item.id}`)}</strong>
                        <span>${escapeHtml(buildItemMeta(item))}</span>
                    </span>
                `;
                fragment.appendChild(button);
            });

            resultsNode.appendChild(fragment);
            syncSummary();
        };

        const setActiveItem = (itemId) => {
            activeItemId = itemId;
            renderResults();
        };

        const resetScannerState = () => {
            setResult("");
            updateStatus(defaultStatus);
            setScannerMode("idle");
            fileInput.value = "";
        };

        const toFriendlyErrorMessage = (error) => {
            const message = typeof error === "string"
                ? error
                : error instanceof Error
                    ? error.message
                    : "";
            const normalized = message.toLowerCase();

            if (normalized.includes("permission") || normalized.includes("notallowederror")) {
                return "Trình duyệt chưa được cấp quyền camera. Hãy cho phép camera rồi thử lại.";
            }

            if (normalized.includes("notfounderror") || normalized.includes("device not found")) {
                return "Không tìm thấy camera phù hợp trên thiết bị này.";
            }

            if (normalized.includes("notreadableerror") || normalized.includes("trackstarterror")) {
                return "Camera đang được ứng dụng khác sử dụng. Hãy đóng ứng dụng đó rồi thử lại.";
            }

            if (normalized.includes("secure") || normalized.includes("https")) {
                return "Camera chỉ hoạt động trên HTTPS hoặc localhost.";
            }

            return "Không thể truy cập camera hoặc khởi động bộ quét QR.";
        };

        const removeAssignedItem = (itemId) => {
            const removedIndex = items.findIndex((item) => item.id === itemId);
            if (removedIndex < 0) {
                return;
            }

            items.splice(removedIndex, 1);
            if (items.length === 0) {
                activeItemId = 0;
                return;
            }

            if (removedIndex < items.length) {
                activeItemId = items[removedIndex].id;
                return;
            }

            activeItemId = items[items.length - 1].id;
        };

        const assignQrToActiveItem = async (qrValue, sourceLabel) => {
            const normalizedValue = normalizeQrValue(qrValue);
            if (!appTechQrPattern.test(normalizedValue)) {
                setResult("");
                updateStatus(invalidQrMessage, "error");
                return false;
            }

            const activeItem = getActiveItem();
            if (!activeItem) {
                setResult(normalizedValue);
                updateStatus("Chưa có vật tư mục tiêu. Hãy tìm kiếm và chọn vật tư trước khi quét QR.", "error");
                return false;
            }

            if (!(antiForgeryToken instanceof HTMLInputElement) || !assignUrl) {
                updateStatus("Không khởi tạo được chức năng gán QR. Hãy tải lại trang.", "error");
                return false;
            }

            if (isAssigning) {
                return false;
            }

            if (normalizedValue === lastAssignedQr && Date.now() - lastAssignedAt < 2500) {
                return false;
            }

            isAssigning = true;
            setResult(normalizedValue);
            updateStatus(`Đang gán QR ${normalizedValue} cho ${activeItem.tenChiTiet || `vật tư #${activeItem.id}`} (${sourceLabel})...`);

            try {
                const response = await fetch(assignUrl, {
                    method: "POST",
                    headers: {
                        "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8",
                        "X-Requested-With": "XMLHttpRequest"
                    },
                    body: new URLSearchParams({
                        __RequestVerificationToken: antiForgeryToken.value,
                        ItemId: `${activeItem.id}`,
                        QRCode: normalizedValue
                    }).toString(),
                    cache: "no-store"
                });

                const payload = await response.json().catch(() => null);
                if (!response.ok || !payload?.succeeded) {
                    updateStatus(payload?.errorMessage || "Không thể gán QR cho vật tư lúc này.", "error");
                    return false;
                }

                lastAssignedQr = normalizedValue;
                lastAssignedAt = Date.now();
                const assignedName = activeItem.tenChiTiet || `vật tư #${activeItem.id}`;
                removeAssignedItem(activeItem.id);
                renderResults();

                if (items.length === 0) {
                    updateStatus(`Đã gán ${normalizedValue} cho ${assignedName}. Danh sách đã hoàn thành.`);
                    if (isRunning) {
                        await stopScanner("");
                    }
                } else {
                    updateStatus(`Đã gán ${normalizedValue} cho ${assignedName}. Đưa QR tiếp theo vào khung quét.`);
                }

                return true;
            } catch {
                updateStatus("Không thể gán QR cho vật tư lúc này. Hãy thử lại.", "error");
                return false;
            } finally {
                isAssigning = false;
            }
        };

        const searchTargets = async () => {
            if (isSearchPending || !searchUrl) {
                return;
            }

            isSearchPending = true;
            searchButton.disabled = true;
            const buttonLabel = searchButton.querySelector(".button-content span:last-child");
            const previousLabel = buttonLabel instanceof HTMLElement ? buttonLabel.textContent : "";
            if (buttonLabel instanceof HTMLElement) {
                buttonLabel.textContent = "Đang tìm";
            }

            try {
                const formData = new FormData(searchForm);
                const params = new URLSearchParams();
                formData.forEach((value, key) => {
                    if (key === "__RequestVerificationToken") {
                        return;
                    }

                    const normalized = `${value || ""}`.trim();
                    if (normalized) {
                        params.append(key, normalized);
                    }
                });

                const response = await fetch(`${searchUrl}?${params.toString()}`, {
                    method: "GET",
                    headers: {
                        "X-Requested-With": "XMLHttpRequest"
                    },
                    cache: "no-store"
                });

                const payload = await response.json().catch(() => null);
                if (!response.ok || payload?.succeeded === false) {
                    hasLoadedResults = false;
                    items = [];
                    totalLoadedItems = 0;
                    activeItemId = 0;
                    renderResults();
                    updateStatus(payload?.errorMessage || "Không thể tải danh sách vật tư. Hãy thử lại.", "error");
                    return;
                }

                items = Array.isArray(payload?.items)
                    ? payload.items.map((item) => ({
                        id: Number(item.id) || 0,
                        tenHangHoa: `${item.tenHangHoa || ""}`.trim(),
                        tenChiTiet: `${item.tenChiTiet || ""}`.trim(),
                        tenKho: `${item.tenKho || ""}`.trim(),
                        viTriLuuKho: `${item.viTriLuuKho || ""}`.trim(),
                        maSoLo: `${item.maSoLo || ""}`.trim(),
                        maPhieuNhap: `${item.maPhieuNhap || ""}`.trim(),
                        qrCode: `${item.qRCode ?? item.qrCode ?? ""}`.trim()
                    })).filter((item) => item.id > 0)
                    : [];
                hasLoadedResults = true;
                totalLoadedItems = items.length;
                activeItemId = items[0]?.id || 0;
                renderResults();

                if (items.length === 0) {
                    updateStatus("Không tìm thấy vật tư phù hợp để đánh mã QR.");
                } else {
                    updateStatus(`Đã nạp ${items.length} vật tư. Bắt đầu quét QR cho mục tiêu đang chọn.`);
                }
            } catch {
                hasLoadedResults = false;
                items = [];
                totalLoadedItems = 0;
                activeItemId = 0;
                renderResults();
                updateStatus("Không thể tải danh sách vật tư. Hãy thử lại.", "error");
            } finally {
                isSearchPending = false;
                searchButton.disabled = false;
                if (buttonLabel instanceof HTMLElement) {
                    buttonLabel.textContent = previousLabel || "Tìm vật tư";
                }
            }
        };

        const closePopup = async () => {
            await stopScanner("");
            popupShell.hidden = true;
            openButton.setAttribute("aria-expanded", "false");
            document.body.classList.remove("menu-open");
            resetScannerState();
        };

        const openPopup = () => {
            popupShell.hidden = false;
            openButton.setAttribute("aria-expanded", "true");
            document.body.classList.add("menu-open");
            resetScannerState();
        };

        openButton.addEventListener("click", () => {
            if (popupShell.hidden) {
                openPopup();
                return;
            }

            void closePopup();
        });

        closeButtons.forEach((button) => {
            button.addEventListener("click", () => {
                void closePopup();
            });
        });

        document.addEventListener("keydown", (event) => {
            if (event.key === "Escape" && !popupShell.hidden) {
                void closePopup();
            }
        });

        searchForm.addEventListener("submit", (event) => {
            event.preventDefault();
            void searchTargets();
        });

        resetButton.addEventListener("click", () => {
            searchForm.reset();
            void searchTargets();
        });

        resultsNode.addEventListener("click", (event) => {
            if (!(event.target instanceof Element)) {
                return;
            }

            const button = event.target.closest("[data-qr-assignment-target-id]");
            if (!(button instanceof HTMLElement)) {
                return;
            }

            const itemId = Number(button.dataset.qrAssignmentTargetId || "0");
            if (itemId > 0) {
                setActiveItem(itemId);
            }
        });

        startButton.addEventListener("click", async () => {
            appendQrDebugLog("qr_click_start", {
                scope: "qr-assignment",
                isRunning,
                isStarting,
                isFileProcessing,
                state: getQrScannerStateName(html5QrCode)
            });
            if (isRunning || isStarting) {
                updateStatus("Camera đang quét QR.");
                return;
            }

            isStarting = true;
            if (isFileProcessing) {
                updateStatus("Hệ thống đang xử lý ảnh QR.");
                isStarting = false;
                return;
            }

            if (items.length === 0) {
                updateStatus("Hãy tìm danh sách vật tư trước khi mở camera quét QR.", "error");
                isStarting = false;
                return;
            }

            if (!window.isSecureContext && window.location.hostname !== "localhost" && window.location.hostname !== "127.0.0.1") {
                updateStatus("Trình duyệt chỉ cho phép mở camera trên HTTPS hoặc localhost.", "error");
                isStarting = false;
                return;
            }

            if (!navigator.mediaDevices?.getUserMedia) {
                updateStatus("Thiết bị hiện tại không hỗ trợ camera trên trình duyệt này.", "error");
                isStarting = false;
                return;
            }

            try {
                await loadHtml5Qrcode();
            } catch {
                updateStatus("Không tải được thư viện quét QR để đánh mã vật tư.", "error");
                isStarting = false;
                return;
            }

            if (!html5QrCode) {
                html5QrCode = createHtml5QrCodeInstance(reader.id);
            }

            try {
                await resetQrScannerInstance(html5QrCode, "qr-assignment");
                const cameras = await ensureCamerasLoaded();
                if (!selectedCameraId && cameras.length > 0) {
                    selectedCameraId = cameras[0].id;
                }

                if (!selectedCameraId) {
                    updateStatus("Không tìm thấy camera để quét QR.", "error");
                    isStarting = false;
                    return;
                }

                setScannerMode("camera");
                updateStatus("Đưa QR vật tư vào giữa khung quét.");

                await startQrScannerCamera(
                    html5QrCode,
                    reader,
                    selectedCameraId,
                    async (decodedText) => {
                        if (isAssigning) {
                            return;
                        }

                        await assignQrToActiveItem(decodedText, "camera");
                    },
                    () => {
                    });

                isRunning = true;
            } catch (error) {
                console.error("QR assignment scanner start failed.", error);
                isRunning = false;
                setScannerMode("idle");
                updateStatus(toFriendlyErrorMessage(error), "error");
            } finally {
                isStarting = false;
            }
        });

        stopButton.addEventListener("click", async () => {
            if (isFileProcessing) {
                fileScanToken += 1;
                isFileProcessing = false;
                setScannerMode("idle");
                updateStatus("Đã dừng xử lý ảnh QR.");
                return;
            }

            await stopScanner();
        });

        fileInput.addEventListener("change", async () => {
            const [file] = fileInput.files || [];
            if (!file) {
                return;
            }

            if (items.length === 0) {
                updateStatus("Hãy tìm danh sách vật tư trước khi quét QR từ ảnh.", "error");
                fileInput.value = "";
                return;
            }

            try {
                await loadHtml5Qrcode();
            } catch {
                updateStatus("Không tải được thư viện quét QR để đọc từ ảnh.", "error");
                fileInput.value = "";
                return;
            }

            if (!html5QrCode) {
                html5QrCode = createHtml5QrCodeInstance(reader.id);
            }

            try {
                if (isRunning) {
                    await stopScanner("");
                }

                isFileProcessing = true;
                const currentFileToken = ++fileScanToken;
                setScannerMode("file");
                updateStatus("Đang đọc mã QR từ ảnh đã chọn...");

                const decodedText = await scanQrFromImageFile(html5QrCode, file);
                if (!isFileProcessing || currentFileToken !== fileScanToken) {
                    return;
                }

                isFileProcessing = false;
                setScannerMode("idle");
                await assignQrToActiveItem(decodedText, "ảnh");
            } catch {
                isFileProcessing = false;
                setScannerMode("idle");
                updateStatus("Không đọc được QR từ ảnh đã chọn. Hãy thử ảnh rõ hơn hoặc ảnh khác.", "error");
            } finally {
                fileInput.value = "";
            }
        });

        cameraSelect.addEventListener("change", async () => {
            selectedCameraId = cameraSelect.value;
            appendQrDebugLog("qr_camera_select_change", {
                scope: "qr-assignment",
                selectedCameraId,
                label: getQrCameraLabel(selectedCameraId, cameraOptions)
            });
            if (!isRunning || !selectedCameraId) {
                return;
            }

            await stopScanner("Đang chuyển camera...");
            startButton.click();
        });

        window.addEventListener("pagehide", () => {
            void stopScanner("");
        });

        renderResults();
        resetScannerState();
    })();

    document.querySelectorAll(".crud-form").forEach((form) => {
        const errorFields = Array.from(form.querySelectorAll(".login-field.has-error"));
        const validationSummary = form.querySelector(".validation-summary");
        const hasSummaryError = Boolean(validationSummary?.textContent?.trim());

        if (errorFields.length === 0 && !hasSummaryError) {
            return;
        }

        const firstErrorField = errorFields[0] || null;
        const tabPanel = firstErrorField?.closest("[data-tab-panel]");
        if (tabPanel instanceof HTMLElement) {
            const tabGroup = tabPanel.closest("[data-tab-group]");
            const tabName = tabPanel.getAttribute("data-tab");
            if (tabGroup && tabName) {
                activateTabInGroup(tabGroup, tabName);
            }
        }

        requestAnimationFrame(() => {
            const scrollTarget = firstErrorField || validationSummary;
            const inputTarget = firstErrorField?.querySelector("input, select, textarea");

            if (scrollTarget instanceof HTMLElement) {
                scrollTarget.scrollIntoView({
                    behavior: "smooth",
                    block: "center",
                    inline: "nearest"
                });
            }

            if (inputTarget instanceof HTMLElement && typeof inputTarget.focus === "function") {
                inputTarget.focus({ preventScroll: true });
            }
        });
    });

    const loginForm = document.querySelector(".login-form");
    if (loginForm) {
        const errorFields = Array.from(loginForm.querySelectorAll(".login-field.has-error"));
        const validationSummary = loginForm.querySelector(".login-validation");
        const installButton = loginForm.querySelector("[data-app-install-button]");
        const installFeedback = loginForm.querySelector("[data-install-feedback]");

        const shakeField = (field) => {
            const shell = field.querySelector(".login-input-shell");
            if (!shell) {
                return;
            }

            shell.classList.remove("shake-error");
            void shell.offsetWidth;
            shell.classList.add("shake-error");
            shell.addEventListener("animationend", () => {
                shell.classList.remove("shake-error");
            }, { once: true });
        };

        if (errorFields.length > 0 || (validationSummary && validationSummary.textContent?.trim())) {
            const firstErrorField = errorFields[0];
            const firstErrorInput = firstErrorField?.querySelector("input, select, textarea");

            requestAnimationFrame(() => {
                firstErrorInput?.focus();
                errorFields.forEach(shakeField);
            });
        }

        if (installButton) {
            let deferredInstallPrompt = null;
            const iosStandalone = window.navigator.standalone === true;
            const isStandalone = iosStandalone || window.matchMedia("(display-mode: standalone)").matches;
            const isIos = /iphone|ipad|ipod/i.test(window.navigator.userAgent);
            const isSecureInstallContext = window.isSecureContext || ["localhost", "127.0.0.1"].includes(window.location.hostname);

            const setInstallFeedback = (message = "", type = "info") => {
                if (!installFeedback) {
                    return;
                }

                installFeedback.textContent = message;
                installFeedback.className = `login-install-feedback ${type}`;
                installFeedback.hidden = !message;
            };

            const setInstallEnabled = (enabled) => {
                installButton.disabled = !enabled;
            };

            const updateInstallState = () => {
                if (isStandalone) {
                    setInstallEnabled(false);
                    setInstallFeedback("Ứng dụng đã được cài trên thiết bị này.", "success");
                    return;
                }

                setInstallEnabled(true);

                if (!isSecureInstallContext) {
                    setInstallFeedback("Cài app chỉ hoạt động khi site chạy bằng HTTPS hoặc localhost.", "error");
                    return;
                }

                if (deferredInstallPrompt) {
                    setInstallFeedback("Thiết bị đã sẵn sàng để cài app.", "info");
                    return;
                }

                if (isIos) {
                    setInstallFeedback("Trên iPhone/iPad, mở menu Chia sẻ rồi chọn \"Thêm vào MH chính\".", "info");
                    return;
                }

                setInstallFeedback("Nếu chưa hiện hộp cài đặt, hãy dùng Chrome/Edge và truy cập site qua HTTPS hợp lệ.", "info");
            };

            window.addEventListener("beforeinstallprompt", (event) => {
                event.preventDefault();
                deferredInstallPrompt = event;
                updateInstallState();
            });

            window.addEventListener("appinstalled", () => {
                deferredInstallPrompt = null;
                setInstallEnabled(false);
                setInstallFeedback("Đã cài app thành công.", "success");
            });

            installButton.addEventListener("click", async () => {
                if (deferredInstallPrompt) {
                    deferredInstallPrompt.prompt();
                    const result = await deferredInstallPrompt.userChoice.catch(() => null);
                    deferredInstallPrompt = null;

                    if (result?.outcome === "accepted") {
                        setInstallEnabled(false);
                        setInstallFeedback("Đã gửi yêu cầu cài app cho trình duyệt.", "success");
                        return;
                    }

                    updateInstallState();
                    return;
                }

                if (!isSecureInstallContext) {
                    setInstallFeedback("IIS cần chạy site bằng HTTPS thì trình duyệt mới cho cài app.", "error");
                    return;
                }

                if (isIos) {
                    setInstallFeedback("Safari không hiện popup cài đặt tự động. Hãy chọn Chia sẻ > Thêm vào MH chính.", "info");
                    return;
                }

                setInstallFeedback("Trình duyệt chưa cho phép cài app ở trang này. Hãy mở site bằng Chrome/Edge qua HTTPS rồi thử lại.", "error");
            });

            updateInstallState();
        }
    }

    if (menuTrigger && menuPopupShell && menuPopup && menuViewport && menuTitle && menuBack && menuDataNode) {
        let rootItems = [];
        try {
            rootItems = JSON.parse(menuDataNode.textContent || "[]");
        } catch {
            rootItems = [];
        }

        const stack = [{ title: "Menu chính", items: rootItems }];
        let currentGrid = menuViewport.querySelector("[data-menu-grid]");
        let isTransitioning = false;

        const normalizePath = (path) => {
            if (!path) {
                return "";
            }

            const [pathname] = path.trim().split(/[?#]/, 1);
            const normalized = (pathname || "").toLowerCase().replace(/\/+$/, "");
            return normalized || "/";
        };

        const isCurrentPath = (url) => {
            const normalizedUrl = normalizePath(url);
            const normalizedCurrent = normalizePath(currentPath);

            return normalizedUrl === normalizedCurrent ||
                (normalizedUrl === "/trang-chu" && (normalizedCurrent === "/" || normalizedCurrent === "/home/index"));
        };

        const containsActiveItem = (item) => {
            if (isCurrentPath(item.url)) {
                return true;
            }

            return Array.isArray(item.children) && item.children.some(containsActiveItem);
        };

        const findActiveTrail = (items, trail = []) => {
            for (const item of items) {
                if (isCurrentPath(item.url)) {
                    return trail;
                }

                if (!Array.isArray(item.children) || item.children.length === 0) {
                    continue;
                }

                const nestedTrail = findActiveTrail(item.children, trail.concat(item));
                if (nestedTrail) {
                    return nestedTrail;
                }
            }

            return null;
        };

        const restoreActiveTrail = () => {
            stack.splice(1);

            const activeTrail = findActiveTrail(rootItems) || [];
            activeTrail.forEach((item) => {
                if (!Array.isArray(item.children) || item.children.length === 0) {
                    return;
                }

                stack.push({
                    title: item.title || "Menu con",
                    items: item.children
                });
            });
        };

        const focusCurrentTile = () => {
            currentGrid?.querySelector(".menu-tile.active, .menu-tile")?.focus();
        };

        const positionPopup = () => {
            menuPopup.style.left = "50%";
            menuPopup.style.top = "50%";
            menuPopup.style.transform = "translate(-50%, -50%)";
        };

        const closeMenu = () => {
            menuPopupShell.hidden = true;
            document.body.classList.remove("menu-open");
            menuTrigger.setAttribute("aria-expanded", "false");
            stack.splice(1);
            isTransitioning = false;
        };

        const updateHeader = () => {
            const currentLevel = stack[stack.length - 1];
            const isRoot = stack.length === 1;
            menuTitle.textContent = currentLevel.title;
            menuBack.hidden = isRoot;
            menuBack.disabled = isRoot;
        };

        const buildGrid = (items) => {
            const grid = document.createElement("div");
            grid.className = "menu-grid";

            items.forEach((item) => {
                const iconClass = item.iconClass || "fa-solid fa-folder-open";
                const hasChildren = Array.isArray(item.children) && item.children.length > 0;
                const element = document.createElement(hasChildren ? "button" : "a");

                element.className = "menu-tile";
                if (containsActiveItem(item)) {
                    element.classList.add("active");
                }

                if (hasChildren) {
                    element.type = "button";
                } else {
                    element.href = item.url || "#";
                }

                const iconWrap = document.createElement("span");
                iconWrap.className = "menu-tile-icon";

                const icon = document.createElement("i");
                icon.className = iconClass;
                icon.setAttribute("aria-hidden", "true");
                iconWrap.appendChild(icon);

                const label = document.createElement("span");
                label.className = "menu-tile-label";
                label.textContent = item.title || "Chức năng";

                element.appendChild(iconWrap);
                element.appendChild(label);

                if (hasChildren) {
                    const meta = document.createElement("span");
                    meta.className = "menu-tile-meta";

                    const metaIcon = document.createElement("i");
                    metaIcon.className = "fa-solid fa-angle-right";
                    metaIcon.setAttribute("aria-hidden", "true");
                    meta.appendChild(metaIcon);

                    element.appendChild(meta);
                }

                if (hasChildren) {
                    element.addEventListener("click", () => {
                        if (isTransitioning) {
                            return;
                        }

                        stack.push({
                            title: item.title || "Menu con",
                            items: item.children
                        });
                        transitionToLevel("forward");
                    });
                } else if (!item.url) {
                    element.addEventListener("click", (event) => {
                        event.preventDefault();
                    });
                }

                grid.appendChild(element);
            });

            return grid;
        };

        const swapGrid = (nextGrid, direction) => {
            const outgoingGrid = currentGrid;

            if (!outgoingGrid) {
                nextGrid.setAttribute("data-menu-grid", "");
                menuViewport.appendChild(nextGrid);
                currentGrid = nextGrid;
                return Promise.resolve();
            }

            isTransitioning = true;

            menuViewport.appendChild(nextGrid);

            outgoingGrid.classList.add("is-transition-layer");
            nextGrid.classList.add("is-transition-layer");

            const incomingOffset = direction === "forward" ? "18%" : "-18%";
            const outgoingOffset = direction === "forward" ? "-18%" : "18%";
            const timing = {
                duration: 260,
                easing: "cubic-bezier(0.22, 1, 0.36, 1)",
                fill: "forwards"
            };

            const incomingAnimation = nextGrid.animate([
                { transform: `translateX(${incomingOffset})`, opacity: 0.35 },
                { transform: "translateX(0)", opacity: 1 }
            ], timing);

            const outgoingAnimation = outgoingGrid.animate([
                { transform: "translateX(0)", opacity: 1 },
                { transform: `translateX(${outgoingOffset})`, opacity: 0 }
            ], timing);

            return Promise.all([incomingAnimation.finished, outgoingAnimation.finished])
                .catch(() => {
                })
                .then(() => {
                    outgoingGrid.remove();
                    nextGrid.classList.remove("is-transition-layer");
                    nextGrid.removeAttribute("style");
                    nextGrid.setAttribute("data-menu-grid", "");
                    currentGrid = nextGrid;
                    isTransitioning = false;
                });
        };

        const renderLevel = (direction = null) => {
            updateHeader();

            const currentLevel = stack[stack.length - 1];
            const nextGrid = buildGrid(currentLevel.items);

            if (!direction) {
                currentGrid?.remove();
                nextGrid.setAttribute("data-menu-grid", "");
                menuViewport.innerHTML = "";
                menuViewport.appendChild(nextGrid);
                currentGrid = nextGrid;
                return Promise.resolve();
            }

            return swapGrid(nextGrid, direction);
        };

        const transitionToLevel = (direction) => {
            renderLevel(direction).then(() => {
                requestAnimationFrame(() => {
                    positionPopup();
                    focusCurrentTile();
                });
            });
        };

        const openMenu = () => {
            if (accountPopupShell && accountTrigger && !accountPopupShell.hidden) {
                accountPopupShell.hidden = true;
                accountTrigger.setAttribute("aria-expanded", "false");
            }

            restoreActiveTrail();
            renderLevel();
            menuPopupShell.hidden = false;
            document.body.classList.add("menu-open");
            menuTrigger.setAttribute("aria-expanded", "true");

            requestAnimationFrame(() => {
                positionPopup();
                focusCurrentTile();
            });
        };

        menuTrigger.addEventListener("click", () => {
            if (menuPopupShell.hidden) {
                openMenu();
                return;
            }

            closeMenu();
        });

        menuBack.addEventListener("click", () => {
            if (stack.length <= 1 || isTransitioning) {
                return;
            }

            stack.pop();
            transitionToLevel("backward");
        });

        menuPopupShell.querySelectorAll("[data-menu-close]").forEach((element) => {
            element.addEventListener("click", () => {
                closeMenu();
            });
        });

        window.addEventListener("resize", () => {
            if (!menuPopupShell.hidden) {
                positionPopup();
            }
        });

        document.addEventListener("keydown", (event) => {
            if (event.key !== "Escape" || menuPopupShell.hidden) {
                return;
            }

            if (stack.length > 1) {
                if (isTransitioning) {
                    return;
                }

                stack.pop();
                transitionToLevel("backward");
                return;
            }

            closeMenu();
        });
    }

    if (crudModalShell) {
        document.body.classList.add("menu-open");

        const closeLink = crudModalShell.querySelector("[data-crud-modal-close]");
        const focusTarget = crudModalShell.querySelector("[autofocus], input, select, textarea, button, a");

        requestAnimationFrame(() => {
            focusTarget?.focus();
        });

        document.addEventListener("keydown", (event) => {
            if (event.key !== "Escape") {
                return;
            }

            if (closeLink instanceof HTMLAnchorElement) {
                window.location.href = closeLink.href;
            }
        });
    }

    if (dataActions) {
        const trigger = dataActions.querySelector("[data-data-actions-trigger]");
        const menu = dataActions.querySelector("[data-data-actions-menu]");
        const importTrigger = dataActions.querySelector("[data-import-trigger]");
        const importForm = document.querySelector("[data-import-form]");
        const importFile = document.querySelector("[data-import-file]");

        const closeDataMenu = () => {
            if (!(trigger instanceof HTMLButtonElement) || !menu) {
                return;
            }

            menu.hidden = true;
            trigger.setAttribute("aria-expanded", "false");
        };

        const openDataMenu = () => {
            if (!(trigger instanceof HTMLButtonElement) || !menu) {
                return;
            }

            menu.hidden = false;
            trigger.setAttribute("aria-expanded", "true");
        };

        if (trigger instanceof HTMLButtonElement && menu) {
            trigger.addEventListener("click", () => {
                if (menu.hidden) {
                    openDataMenu();
                    return;
                }

                closeDataMenu();
            });

            document.addEventListener("click", (event) => {
                if (!dataActions.contains(event.target)) {
                    closeDataMenu();
                }
            });

            document.addEventListener("keydown", (event) => {
                if (event.key === "Escape") {
                    closeDataMenu();
                }
            });
        }

        if (importTrigger instanceof HTMLButtonElement && importFile instanceof HTMLInputElement) {
            importTrigger.addEventListener("click", () => {
                closeDataMenu();
                importFile.click();
            });
        }

        if (importFile instanceof HTMLInputElement && importForm instanceof HTMLFormElement) {
            importFile.addEventListener("change", () => {
                const [file] = importFile.files || [];
                if (!file) {
                    return;
                }

                importForm.submit();
            });
        }
    }

    const bindStatusToast = (toast) => {
        if (!(toast instanceof HTMLElement) || toast.dataset.statusToastBound === "true") {
            return;
        }

        toast.dataset.statusToastBound = "true";
        const closeButton = toast.querySelector("[data-status-toast-close]");
        let dismissHandle = 0;

        const dismissToast = () => {
            toast.classList.add("is-dismissing");
            window.setTimeout(() => {
                toast.remove();
            }, 220);
        };

        dismissHandle = window.setTimeout(dismissToast, 5000);

        closeButton?.addEventListener("click", () => {
            window.clearTimeout(dismissHandle);
            dismissToast();
        });
    };

    const showStatusToast = (title, message, type = "success") => {
        const toast = document.createElement("div");
        toast.className = `floating-status-toast ${type}`;
        toast.setAttribute("data-status-toast", "");
        toast.setAttribute("role", "status");
        toast.setAttribute("aria-live", "polite");
        toast.innerHTML = `
            <div class="floating-status-toast-icon" aria-hidden="true">
                <i class="fa-solid ${type === "error" ? "fa-circle-exclamation" : type === "info" ? "fa-circle-info" : "fa-circle-check"}"></i>
            </div>
            <div class="floating-status-toast-body">
                <strong>${title}</strong>
                <span>${message}</span>
            </div>
            <button type="button" class="floating-status-toast-close" data-status-toast-close aria-label="Đóng thông báo">
                <i class="fa-solid fa-xmark" aria-hidden="true"></i>
            </button>
        `;

        document.body.appendChild(toast);
        bindStatusToast(toast);
    };

    document.querySelectorAll("[data-status-toast]").forEach(bindStatusToast);

    const copyTextToClipboard = async (value) => {
        const normalizedValue = `${value || ""}`.trim();
        if (!normalizedValue) {
            throw new Error("empty");
        }

        if (navigator.clipboard?.writeText) {
            await navigator.clipboard.writeText(normalizedValue);
            return;
        }

        const helper = document.createElement("textarea");
        helper.value = normalizedValue;
        helper.setAttribute("readonly", "");
        helper.style.position = "fixed";
        helper.style.top = "-1000px";
        helper.style.opacity = "0";
        document.body.appendChild(helper);
        helper.focus();
        helper.select();

        const copied = document.execCommand("copy");
        helper.remove();

        if (!copied) {
            throw new Error("copy_failed");
        }
    };

    document.querySelectorAll("[data-copy-to-clipboard]").forEach((button) => {
        if (!(button instanceof HTMLButtonElement)) {
            return;
        }

        button.addEventListener("click", async () => {
            const value = button.dataset.copyValue || "";
            const successTitle = button.dataset.copyTitle || "Đã copy";
            const successMessage = button.dataset.copyMessage || value;

            try {
                await copyTextToClipboard(value);
                showStatusToast(successTitle, successMessage, "success");
            } catch {
                showStatusToast("Không thể copy", "Trình duyệt chưa cho phép sao chép vào clipboard.", "error");
            }
        });
    });

    document.querySelectorAll("[data-delete-confirm]").forEach((button) => {
        button.addEventListener("click", (event) => {
            const message = button.getAttribute("data-delete-confirm") || "Bạn có chắc muốn xóa dữ liệu này?";
            if (!window.confirm(message)) {
                event.preventDefault();
            }
        });
    });

    document.querySelectorAll("[data-vat-tu-group-toggle]").forEach((button) => {
        if (!(button instanceof HTMLButtonElement)) {
            return;
        }

        const groupId = button.dataset.vatTuGroupId || "";
        if (!groupId) {
            return;
        }

        const detailRows = Array.from(document.querySelectorAll(`[data-vat-tu-group-body="${groupId}"]`));
        if (detailRows.length === 0) {
            return;
        }

        button.addEventListener("click", () => {
            const isExpanded = button.getAttribute("aria-expanded") === "true";
            const nextExpanded = !isExpanded;
            button.setAttribute("aria-expanded", String(nextExpanded));

            detailRows.forEach((row) => {
                if (row instanceof HTMLElement) {
                    row.hidden = !nextExpanded;
                }
            });
        });
    });

    const vatTuSelectAll = document.querySelector("[data-vat-tu-select-all]");
    const vatTuSelectionRows = Array.from(document.querySelectorAll("[data-vat-tu-selection-row]"));
    const vatTuSelectionCards = Array.from(document.querySelectorAll("[data-vat-tu-select-card]"));
    const vatTuSelectionCheckboxes = Array.from(document.querySelectorAll("[data-vat-tu-select-checkbox]"));
    const vatTuCopyOpenButton = document.querySelector("[data-vat-tu-copy-open]");
    const vatTuCopyShell = document.querySelector("[data-vat-tu-copy-shell]");
    const vatTuCopyForm = vatTuCopyShell?.querySelector("[data-vat-tu-copy-form]");
    const vatTuCopyQuantityInput = vatTuCopyShell?.querySelector("[data-vat-tu-copy-quantity]");
    const vatTuCopySelectedIdsInput = vatTuCopyShell?.querySelector("[data-vat-tu-copy-selected-ids]");
    const vatTuCopySummary = vatTuCopyShell?.querySelector("[data-vat-tu-copy-summary] span");
    const vatTuCopyCloseButtons = vatTuCopyShell ? Array.from(vatTuCopyShell.querySelectorAll("[data-vat-tu-copy-close]")) : [];
    const vatTuSelectedCountNodes = Array.from(document.querySelectorAll("[data-vat-tu-selected-count]"));

    if (vatTuSelectionRows.length > 0 || vatTuSelectionCards.length > 0) {
        const selectedIds = new Set();
        const itemIds = Array.from(new Set(
            [
                ...vatTuSelectionRows.map((row) => row.getAttribute("data-vat-tu-id") || ""),
                ...vatTuSelectionCards.map((card) => card.getAttribute("data-vat-tu-id") || "")
            ].filter(Boolean)
        ));

        const syncCopySummary = () => {
            if (!(vatTuCopySummary instanceof HTMLElement)) {
                return;
            }

            const selectionCount = selectedIds.size;
            const copyQuantity = Math.max(1, Number(vatTuCopyQuantityInput instanceof HTMLInputElement ? vatTuCopyQuantityInput.value : "1") || 1);
            const totalCopies = selectionCount * copyQuantity;

            if (selectionCount === 0) {
                vatTuCopySummary.textContent = "Chọn vật tư cần copy trước khi xác nhận.";
                return;
            }

            vatTuCopySummary.textContent = `Đang chọn ${selectionCount} vật tư. Hệ thống sẽ tạo ${totalCopies} vật tư mới, không kèm mã QR và hình ảnh.`;
        };

        const closeCopyPopup = () => {
            if (!(vatTuCopyShell instanceof HTMLElement) || vatTuCopyShell.hidden) {
                return;
            }

            vatTuCopyShell.hidden = true;
            if (!crudModalShell) {
                document.body.classList.remove("menu-open");
            }
        };

        const syncSelectionState = () => {
            vatTuSelectionCheckboxes.forEach((checkbox) => {
                const id = checkbox.value;
                checkbox.checked = selectedIds.has(id);
            });

            vatTuSelectionRows.forEach((row) => {
                const id = row.getAttribute("data-vat-tu-id") || "";
                row.classList.toggle("is-selected", selectedIds.has(id));
            });

            vatTuSelectionCards.forEach((card) => {
                const id = card.getAttribute("data-vat-tu-id") || "";
                const isSelected = selectedIds.has(id);
                const isSelectionMode = selectedIds.size > 0;
                card.classList.toggle("is-selection-mode", isSelectionMode);
                card.classList.toggle("is-selected", isSelected);

                const surface = card.querySelector("[data-vat-tu-select-surface]");
                if (surface instanceof HTMLElement) {
                    surface.setAttribute("aria-pressed", String(isSelected));
                }
            });

            if (vatTuSelectAll instanceof HTMLInputElement) {
                const selectedCount = itemIds.filter((id) => selectedIds.has(id)).length;
                vatTuSelectAll.checked = itemIds.length > 0 && selectedCount === itemIds.length;
                vatTuSelectAll.indeterminate = selectedCount > 0 && selectedCount < itemIds.length;
            }

            vatTuSelectedCountNodes.forEach((node) => {
                node.textContent = `${selectedIds.size}`;
            });

            if (vatTuCopyOpenButton instanceof HTMLButtonElement) {
                vatTuCopyOpenButton.disabled = selectedIds.size === 0;
            }

            if (vatTuCopySelectedIdsInput instanceof HTMLInputElement) {
                vatTuCopySelectedIdsInput.value = Array.from(selectedIds).join(",");
            }

            if (selectedIds.size === 0) {
                closeCopyPopup();
            }

            syncCopySummary();
        };

        const toggleSelection = (id, nextState) => {
            if (!id) {
                return;
            }

            const shouldSelect = typeof nextState === "boolean" ? nextState : !selectedIds.has(id);
            if (shouldSelect) {
                selectedIds.add(id);
            } else {
                selectedIds.delete(id);
            }

            syncSelectionState();
        };

        const openCopyPopup = () => {
            if (!(vatTuCopyShell instanceof HTMLElement) || selectedIds.size === 0) {
                return;
            }

            vatTuCopyShell.hidden = false;
            document.body.classList.add("menu-open");
            syncCopySummary();

            requestAnimationFrame(() => {
                if (vatTuCopyQuantityInput instanceof HTMLInputElement) {
                    vatTuCopyQuantityInput.focus();
                    vatTuCopyQuantityInput.select();
                }
            });
        };

        if (vatTuSelectAll instanceof HTMLInputElement) {
            vatTuSelectAll.addEventListener("change", () => {
                itemIds.forEach((id) => {
                    if (vatTuSelectAll.checked) {
                        selectedIds.add(id);
                    } else {
                        selectedIds.delete(id);
                    }
                });

                syncSelectionState();
            });
        }

        vatTuSelectionCheckboxes.forEach((checkbox) => {
            checkbox.addEventListener("change", () => {
                toggleSelection(checkbox.value, checkbox.checked);
            });
        });

        vatTuSelectionCards.forEach((card) => {
            const id = card.getAttribute("data-vat-tu-id") || "";
            const surface = card.querySelector("[data-vat-tu-select-surface]");
            if (!(surface instanceof HTMLElement) || !id) {
                return;
            }

            let pressTimer = 0;
            let longPressTriggered = false;
            let startX = 0;
            let startY = 0;

            const clearPressTimer = () => {
                if (pressTimer) {
                    window.clearTimeout(pressTimer);
                    pressTimer = 0;
                }
            };

            surface.addEventListener("contextmenu", (event) => {
                event.preventDefault();
            });

            surface.addEventListener("dragstart", (event) => {
                event.preventDefault();
            });

            surface.addEventListener("pointerdown", (event) => {
                if (event.pointerType === "mouse" && event.button !== 0) {
                    return;
                }

                longPressTriggered = false;
                startX = event.clientX;
                startY = event.clientY;
                clearPressTimer();

                pressTimer = window.setTimeout(() => {
                    longPressTriggered = true;
                    toggleSelection(id);

                    if (typeof navigator.vibrate === "function") {
                        navigator.vibrate(30);
                    }
                }, 420);
            });

            surface.addEventListener("pointermove", (event) => {
                if (!pressTimer) {
                    return;
                }

                if (Math.abs(event.clientX - startX) > 10 || Math.abs(event.clientY - startY) > 10) {
                    clearPressTimer();
                }
            });

            ["pointerup", "pointercancel", "pointerleave", "lostpointercapture"].forEach((eventName) => {
                surface.addEventListener(eventName, () => {
                    clearPressTimer();
                });
            });

            surface.addEventListener("click", (event) => {
                if (longPressTriggered) {
                    event.preventDefault();
                    event.stopPropagation();
                    longPressTriggered = false;
                    return;
                }

                if (selectedIds.size === 0) {
                    return;
                }

                event.preventDefault();
                event.stopPropagation();
                toggleSelection(id);
            }, true);
        });

        if (vatTuCopyOpenButton instanceof HTMLButtonElement) {
            vatTuCopyOpenButton.addEventListener("click", () => {
                openCopyPopup();
            });
        }

        if (vatTuCopyQuantityInput instanceof HTMLInputElement) {
            vatTuCopyQuantityInput.addEventListener("input", () => {
                syncCopySummary();
            });
        }

        vatTuCopyCloseButtons.forEach((button) => {
            button.addEventListener("click", () => {
                closeCopyPopup();
            });
        });

        if (vatTuCopyForm instanceof HTMLFormElement) {
            vatTuCopyForm.addEventListener("submit", (event) => {
                const copyQuantity = Math.max(0, Number(vatTuCopyQuantityInput instanceof HTMLInputElement ? vatTuCopyQuantityInput.value : "0") || 0);
                if (selectedIds.size === 0 || copyQuantity <= 0) {
                    event.preventDefault();

                    if (vatTuCopyQuantityInput instanceof HTMLInputElement && copyQuantity <= 0) {
                        vatTuCopyQuantityInput.focus();
                    }
                }
            });
        }

        document.addEventListener("keydown", (event) => {
            if (event.key === "Escape" && vatTuCopyShell instanceof HTMLElement && !vatTuCopyShell.hidden) {
                closeCopyPopup();
            }
        });

        syncSelectionState();
    }

    const vatTuSwipeCards = Array.from(document.querySelectorAll("[data-vat-tu-swipe]"));
    if (vatTuSwipeCards.length > 0) {
        const maxSwipeOffset = 86;
        let activeSwipeCard = null;

        const setSwipeOffset = (card, offset, withAnimation = true) => {
            const surface = card.querySelector("[data-vat-tu-swipe-surface]");
            if (!(surface instanceof HTMLElement)) {
                return;
            }

            surface.style.transition = withAnimation ? "transform 280ms cubic-bezier(0.22, 1, 0.36, 1)" : "none";
            surface.style.transform = `translate3d(${offset}px, 0, 0)`;
            card.classList.toggle("is-delete-open", offset <= -(maxSwipeOffset - 4));
            card.dataset.swipeOffset = `${offset}`;
        };

        const closeSwipeCard = (card, withAnimation = true) => {
            setSwipeOffset(card, 0, withAnimation);
            if (activeSwipeCard === card) {
                activeSwipeCard = null;
            }
        };

        const openSwipeCard = (card) => {
            vatTuSwipeCards.forEach((otherCard) => {
                if (otherCard !== card) {
                    closeSwipeCard(otherCard);
                }
            });

            setSwipeOffset(card, -maxSwipeOffset);
            activeSwipeCard = card;
        };

        vatTuSwipeCards.forEach((card) => {
            const surface = card.querySelector("[data-vat-tu-swipe-surface]");
            if (!(surface instanceof HTMLElement)) {
                return;
            }

            let startX = 0;
            let startY = 0;
            let initialOffset = 0;
            let tracking = false;
            let dragging = false;
            let suppressClick = false;

            const getCurrentOffset = () => Number(card.dataset.swipeOffset || "0");

            const finishSwipe = (cancelled = false) => {
                if (!tracking && !dragging) {
                    return;
                }

                const currentOffset = getCurrentOffset();
                const shouldOpen = !cancelled && dragging && currentOffset <= -(maxSwipeOffset / 2);

                if (shouldOpen) {
                    openSwipeCard(card);
                } else {
                    closeSwipeCard(card);
                }

                tracking = false;
                dragging = false;
            };

            surface.addEventListener("pointerdown", (event) => {
                if (event.pointerType === "mouse" && event.button !== 0) {
                    return;
                }

                tracking = true;
                dragging = false;
                suppressClick = false;
                startX = event.clientX;
                startY = event.clientY;
                initialOffset = getCurrentOffset();
                surface.setPointerCapture?.(event.pointerId);

                if (activeSwipeCard && activeSwipeCard !== card) {
                    closeSwipeCard(activeSwipeCard);
                }
            });

            surface.addEventListener("pointermove", (event) => {
                if (!tracking) {
                    return;
                }

                const deltaX = event.clientX - startX;
                const deltaY = event.clientY - startY;

                if (!dragging) {
                    if (Math.abs(deltaX) < 8) {
                        return;
                    }

                    if (Math.abs(deltaX) <= Math.abs(deltaY)) {
                        tracking = false;
                        return;
                    }

                    dragging = true;
                    suppressClick = true;
                }

                let nextOffset = initialOffset + deltaX;
                if (nextOffset < -maxSwipeOffset) {
                    nextOffset = -maxSwipeOffset - ((Math.abs(nextOffset) - maxSwipeOffset) * 0.18);
                }
                nextOffset = Math.max(-maxSwipeOffset - 12, Math.min(0, nextOffset));
                setSwipeOffset(card, nextOffset, false);
            });

            surface.addEventListener("pointerup", () => {
                finishSwipe(false);
            });

            surface.addEventListener("pointercancel", () => {
                finishSwipe(true);
            });

            surface.addEventListener("lostpointercapture", () => {
                finishSwipe(false);
            });

            surface.addEventListener("click", (event) => {
                if (suppressClick) {
                    event.preventDefault();
                    event.stopPropagation();
                    suppressClick = false;
                    return;
                }

                if (card.classList.contains("is-delete-open")) {
                    event.preventDefault();
                    event.stopPropagation();
                    closeSwipeCard(card);
                }
            });
        });

        document.addEventListener("click", (event) => {
            if (!(event.target instanceof Node)) {
                return;
            }

            vatTuSwipeCards.forEach((card) => {
                if (!card.contains(event.target)) {
                    closeSwipeCard(card);
                }
            });
        });
    }

    const canvas = document.getElementById("energyChart");
    if (canvas) {
        const ctx = canvas.getContext("2d");
        const values = (canvas.dataset.series || "")
            .split(",")
            .map(Number)
            .filter((n) => !Number.isNaN(n));

        const resizeAndDraw = () => {
            const rect = canvas.getBoundingClientRect();
            canvas.width = rect.width * window.devicePixelRatio;
            canvas.height = rect.height * window.devicePixelRatio;
            ctx.setTransform(window.devicePixelRatio, 0, 0, window.devicePixelRatio, 0, 0);
            drawLineChart(ctx, rect.width, rect.height, values);
        };

        resizeAndDraw();
        window.addEventListener("resize", resizeAndDraw);
    }

    const donut = document.getElementById("deviceDonut");
    if (donut) {
        const palette = ["#21bf96", "#42b8d4", "#184665", "#88c845", "#c6487e"];
        const values = (donut.dataset.values || "")
            .split(",")
            .map(Number)
            .filter((n) => !Number.isNaN(n));

        const total = values.reduce((sum, n) => sum + n, 0);
        if (total > 0) {
            let current = 0;
            const segments = values.map((value, index) => {
                const angle = (value / total) * 360;
                const segment = `${palette[index % palette.length]} ${current}deg ${current + angle}deg`;
                current += angle;
                return segment;
            });
            donut.style.background = `conic-gradient(${segments.join(", ")})`;
        }
    }

    function drawLineChart(ctx, width, height, values) {
        ctx.clearRect(0, 0, width, height);

        const padding = { top: 24, right: 16, bottom: 28, left: 38 };
        const chartWidth = width - padding.left - padding.right;
        const chartHeight = height - padding.top - padding.bottom;

        const min = Math.min(...values) - 10;
        const max = Math.max(...values) + 10;

        ctx.strokeStyle = "rgba(23, 48, 66, 0.12)";
        ctx.lineWidth = 1;
        for (let i = 0; i <= 4; i++) {
            const y = padding.top + (chartHeight / 4) * i;
            ctx.beginPath();
            ctx.moveTo(padding.left, y);
            ctx.lineTo(width - padding.right, y);
            ctx.stroke();
        }

        ctx.fillStyle = "#6c8293";
        ctx.font = "12px Segoe UI";
        ["T1", "T3", "T5", "T7", "T9", "T11"].forEach((label, index) => {
            const x = padding.left + (chartWidth / 5) * index;
            ctx.fillText(label, x - 8, height - 8);
        });

        const points = values.map((value, index) => {
            const x = padding.left + (chartWidth / (values.length - 1)) * index;
            const y = padding.top + ((max - value) / (max - min)) * chartHeight;
            return { x, y };
        });

        const gradient = ctx.createLinearGradient(0, padding.top, 0, padding.top + chartHeight);
        gradient.addColorStop(0, "rgba(33, 191, 150, 0.28)");
        gradient.addColorStop(1, "rgba(66, 184, 212, 0.03)");

        ctx.beginPath();
        ctx.moveTo(points[0].x, points[0].y);
        for (let i = 1; i < points.length; i++) {
            const prev = points[i - 1];
            const current = points[i];
            const cpx = (prev.x + current.x) / 2;
            ctx.bezierCurveTo(cpx, prev.y, cpx, current.y, current.x, current.y);
        }
        ctx.lineTo(points[points.length - 1].x, height - padding.bottom);
        ctx.lineTo(points[0].x, height - padding.bottom);
        ctx.closePath();
        ctx.fillStyle = gradient;
        ctx.fill();

        ctx.beginPath();
        ctx.moveTo(points[0].x, points[0].y);
        for (let i = 1; i < points.length; i++) {
            const prev = points[i - 1];
            const current = points[i];
            const cpx = (prev.x + current.x) / 2;
            ctx.bezierCurveTo(cpx, prev.y, cpx, current.y, current.x, current.y);
        }
        ctx.strokeStyle = "#21bf96";
        ctx.lineWidth = 3;
        ctx.stroke();

        ctx.fillStyle = "#184665";
        points.forEach((point) => {
            ctx.beginPath();
            ctx.arc(point.x, point.y, 4.5, 0, Math.PI * 2);
            ctx.fill();
            ctx.beginPath();
            ctx.arc(point.x, point.y, 2.5, 0, Math.PI * 2);
            ctx.fillStyle = "#ffffff";
            ctx.fill();
            ctx.fillStyle = "#184665";
        });
    }

    (() => {
        const form = document.querySelector("[data-xuat-kho-form]");
        const root = document.querySelector("[data-xuat-kho-root]");
        const list = root?.querySelector("[data-xuat-kho-detail-list]");
        const template = document.querySelector("[data-xuat-kho-detail-template]");
        const emptyState = root?.querySelector("[data-xuat-kho-detail-empty]");
        const statusNode = root?.querySelector("[data-xuat-kho-qr-status] span") || root?.querySelector("[data-xuat-kho-qr-status]");
        const manualInput = root?.querySelector("[data-xuat-kho-qr-manual]");
        const addManualButton = root?.querySelector("[data-xuat-kho-qr-add]");
        const startButton = root?.querySelector("[data-xuat-kho-qr-start]");
        const stopButton = root?.querySelector("[data-xuat-kho-qr-stop]");
        const fileInput = root?.querySelector("[data-xuat-kho-qr-file]");
        const panel = root?.querySelector("[data-xuat-kho-qr-panel]");
        const reader = root?.querySelector("[data-xuat-kho-qr-reader]");
        const cameraShell = root?.querySelector("[data-xuat-kho-camera-shell]");
        const cameraSelect = root?.querySelector("[data-xuat-kho-camera]");
        const summary = root?.querySelector("[data-xuat-kho-qr-summary]");
        const summaryDefault = root?.querySelector("[data-xuat-kho-qr-summary-default]");
        const searchInput = root?.querySelector("[data-xuat-kho-search-input]");
        const searchResults = root?.querySelector("[data-xuat-kho-search-results]");
        const scannerToggle = root?.querySelector("[data-xuat-kho-scanner-toggle]");
        const scannerBody = root?.querySelector("[data-xuat-kho-scanner-body]");

        if (!(form instanceof HTMLFormElement) ||
            !(root instanceof HTMLElement) ||
            !(list instanceof HTMLElement) ||
            !(template instanceof HTMLTemplateElement)) {
            return;
        }

        const clientErrorBox = form.querySelector("[data-xuat-kho-client-errors]");
        const clientErrorList = form.querySelector("[data-xuat-kho-client-error-list]");
        const activeTabInput = form.querySelector("[data-active-tab-input]");
        const isReadonly = root.getAttribute("data-readonly") === "true";
        let html5QrCode = null;
        let isRunning = false;
        let isFileProcessing = false;
        let isStarting = false;
        let cameraOptions = [];
        let selectedCameraId = "";
        let fileScanToken = 0;
        let searchDebounceTimer = 0;
        let searchRequestToken = 0;

        const escapeHtml = (value) => `${value || ""}`
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll("\"", "&quot;")
            .replaceAll("'", "&#39;");

        if (reader instanceof HTMLElement && !reader.id) {
            reader.id = "xuat-kho-qr-reader";
        }

        const updateStatus = (message) => {
            if (statusNode instanceof HTMLElement) {
                statusNode.textContent = message;
            }
        };

        const parseNumber = (value) => {
            const parsed = Number(`${value || ""}`.replace(",", "."));
            return Number.isFinite(parsed) ? parsed : 0;
        };

        const formatNumber = (value) => {
            const parsed = parseNumber(value);
            return parsed.toLocaleString("vi-VN", { maximumFractionDigits: 2 });
        };

        const getRows = () => Array.from(list.querySelectorAll("[data-xuat-kho-detail-row]"));

        const setProcessingState = (button, isProcessing) => {
            if (!(button instanceof HTMLButtonElement)) {
                return;
            }

            button.classList.toggle("is-processing", isProcessing);
            if (isProcessing) {
                button.setAttribute("aria-busy", "true");
            } else {
                button.removeAttribute("aria-busy");
            }
        };

        const showClientErrors = (messages) => {
            if (!(clientErrorBox instanceof HTMLElement) || !(clientErrorList instanceof HTMLElement)) {
                return;
            }

            clientErrorList.innerHTML = messages
                .map((message) => `<div>${escapeHtml(message)}</div>`)
                .join("");
            clientErrorBox.hidden = messages.length === 0;
            clientErrorBox.classList.toggle("validation-summary", messages.length > 0);
            if (messages.length > 0) {
                clientErrorBox.scrollIntoView({ block: "nearest", behavior: "smooth" });
            }
        };

        const setFieldError = (selector, hasError) => {
            const field = form.querySelector(selector);
            if (field instanceof HTMLElement) {
                field.classList.toggle("has-error", hasError);
            }
        };

        const updateEmptyState = () => {
            const hasRows = getRows().length > 0;
            emptyState?.classList.toggle("is-hidden", hasRows);
            if (hasRows) {
                root.classList.remove("has-error");
            }
        };

        const setInputValue = (row, selector, value) => {
            const input = row.querySelector(selector);
            if (input instanceof HTMLInputElement) {
                input.value = value == null ? "" : `${value}`;
            }
        };

        const setText = (row, selector, value, fallback = "-") => {
            const node = row.querySelector(selector);
            if (node instanceof HTMLElement) {
                const text = value == null ? "" : `${value}`.trim();
                node.textContent = text || fallback;
            }
        };

        const fillRow = (row, item) => {
            const stock = parseNumber(item.soLuongTon);
            const quantity = parseNumber(item.soLuongXuat) || stock || 1;

            row.setAttribute("data-vat-tu-id", `${item.vatTuId || ""}`);
            setInputValue(row, "[data-detail-id]", item.id || "");
            setInputValue(row, "[data-detail-vat-tu-id]", item.vatTuId || "");
            setInputValue(row, "[data-detail-hang-hoa-id]", item.hangHoaId || "");
            setInputValue(row, "[data-detail-ten-chi-tiet]", item.tenChiTiet || "");
            setInputValue(row, "[data-detail-ten-hang-hoa]", item.tenHangHoa || "");
            setInputValue(row, "[data-detail-ma-hang-hoa]", item.maHangHoa || "");
            setInputValue(row, "[data-detail-ten-kho]", item.tenKho || "");
            setInputValue(row, "[data-detail-ma-kho]", item.maKho || "");
            setInputValue(row, "[data-detail-don-vi-tinh]", item.donViTinh || "");
            setInputValue(row, "[data-detail-qr-code]", item.qrCode || "");
            setInputValue(row, "[data-detail-so-luong-ton]", stock);

            setText(row, "[data-detail-display-name]", item.tenChiTiet);
            setText(row, "[data-detail-display-product]", item.tenHangHoa);
            setText(row, "[data-detail-display-kho]", item.tenKho);
            setText(row, "[data-detail-display-stock]", formatNumber(stock), "0");
            setText(row, "[data-detail-display-unit]", item.donViTinh, "");

            const detailLink = row.querySelector("[data-detail-link]");
            if (detailLink instanceof HTMLAnchorElement) {
                detailLink.href = `/VatTu?editId=${encodeURIComponent(item.vatTuId || "")}&page=1`;
            }

            const quantityInput = row.querySelector("[data-detail-quantity]");
            if (quantityInput instanceof HTMLInputElement) {
                quantityInput.value = `${Math.min(Math.max(quantity, 1), Math.max(stock, 1))}`;
                quantityInput.max = `${stock}`;
                quantityInput.readOnly = isReadonly;
            }
        };

        const reindexRows = () => {
            getRows().forEach((row, index) => {
                row.querySelectorAll("input[name]").forEach((input) => {
                    const currentName = input.getAttribute("name") || "";
                    input.setAttribute("name", currentName.replace(/Form\.Details\[(?:\d+|__index__)\]/u, `Form.Details[${index}]`));
                });
            });
        };

        form.addEventListener("submit", (event) => {
            if (event.defaultPrevented) {
                return;
            }

            const messages = [];
            const noiDungInput = form.querySelector("[name='Form.NoiDungXuatKho']");
            const ngayXuatKhoInput = form.querySelector("[name='Form.NgayXuatKho']");
            const submitter = event.submitter instanceof HTMLButtonElement ? event.submitter : null;
            let hasNoiDungError = false;
            let hasNgayXuatKhoError = false;
            let hasDetailError = false;

            if (noiDungInput instanceof HTMLTextAreaElement && !noiDungInput.value.trim()) {
                messages.push("Vui lòng nhập nội dung xuất kho.");
                hasNoiDungError = true;
            }

            if (ngayXuatKhoInput instanceof HTMLInputElement && ngayXuatKhoInput.value) {
                const selectedDate = new Date(`${ngayXuatKhoInput.value}T00:00:00`);
                const today = new Date();
                today.setHours(0, 0, 0, 0);
                if (selectedDate > today) {
                    messages.push("Ngày xuất kho không được vượt quá ngày hiện tại.");
                    hasNgayXuatKhoError = true;
                }
            }

            if (getRows().length === 0) {
                messages.push("Vui lòng scan hoặc thêm ít nhất một vật tư cần xuất.");
                hasDetailError = true;
                if (activeTabInput instanceof HTMLInputElement) {
                    activeTabInput.value = "vat-tu-xuat";
                }
            }

            setFieldError("[data-xuat-kho-field='noi-dung']", hasNoiDungError);
            setFieldError("[data-xuat-kho-field='ngay-xuat-kho']", hasNgayXuatKhoError);
            root.classList.toggle("has-error", hasDetailError);

            if (messages.length === 0) {
                showClientErrors([]);
                return;
            }

            event.preventDefault();
            setProcessingState(submitter, false);
            showClientErrors(messages);
        });

        const noiDungInput = form.querySelector("[name='Form.NoiDungXuatKho']");
        if (noiDungInput instanceof HTMLTextAreaElement) {
            noiDungInput.addEventListener("input", () => {
                if (noiDungInput.value.trim()) {
                    setFieldError("[data-xuat-kho-field='noi-dung']", false);
                }
            });
        }

        const ngayXuatKhoInput = form.querySelector("[name='Form.NgayXuatKho']");
        if (ngayXuatKhoInput instanceof HTMLInputElement) {
            ngayXuatKhoInput.addEventListener("change", () => {
                setFieldError("[data-xuat-kho-field='ngay-xuat-kho']", false);
            });
        }

        const clampQuantity = (input) => {
            const max = parseNumber(input.max);
            let value = parseNumber(input.value);
            if (value < 1) {
                value = 1;
            }

            if (max > 0 && value > max) {
                value = max;
            }

            input.value = `${value}`;
        };

        const attachRowEvents = (row) => {
            const removeButton = row.querySelector("[data-xuat-kho-remove-detail]");
            if (removeButton instanceof HTMLButtonElement) {
                removeButton.addEventListener("click", () => {
                    row.remove();
                    reindexRows();
                    updateEmptyState();
                });
            }

            const quantityInput = row.querySelector("[data-detail-quantity]");
            if (quantityInput instanceof HTMLInputElement) {
                quantityInput.addEventListener("change", () => clampQuantity(quantityInput));
                quantityInput.addEventListener("blur", () => clampQuantity(quantityInput));
            }
        };

        const addDetailItem = (item) => {
            const vatTuId = Number(item?.vatTuId || 0);
            if (!vatTuId) {
                updateStatus("Dữ liệu vật tư không hợp lệ.");
                return false;
            }

            const existingRow = getRows().find((row) => row.getAttribute("data-vat-tu-id") === `${vatTuId}`);
            if (existingRow instanceof HTMLElement) {
                updateStatus("Vật tư này đã có trong phiếu xuất.");
                existingRow.classList.add("is-selected");
                setTimeout(() => existingRow.classList.remove("is-selected"), 900);
                return false;
            }

            const fragment = template.content.cloneNode(true);
            const row = fragment.firstElementChild;
            if (!(row instanceof HTMLElement)) {
                return false;
            }

            fillRow(row, item);
            list.appendChild(row);
            attachRowEvents(row);
            reindexRows();
            updateEmptyState();
            updateStatus(`Đã thêm ${item.tenChiTiet || "vật tư"} vào phiếu xuất.`);
            return true;
        };

        const lookupAndAddQr = async (qrValue) => {
            const normalizedValue = `${qrValue || ""}`.trim();
            if (!normalizedValue) {
                updateStatus("Vui lòng scan hoặc nhập mã QR vật tư.");
                return false;
            }

            const now = Date.now();
            const recentScanTime = recentQrValues.get(normalizedValue) || 0;
            if (now - recentScanTime < 1600 || isLookupProcessing) {
                return false;
            }

            recentQrValues.set(normalizedValue, now);
            isLookupProcessing = true;
            updateStatus("Đang kiểm tra QR vật tư...");
            try {
                const response = await fetch(`/XuatKho/FindVatTuByQrCode?value=${encodeURIComponent(normalizedValue)}`, {
                    method: "GET",
                    headers: {
                        "X-Requested-With": "XMLHttpRequest"
                    }
                });

                if (!response.ok) {
                    updateStatus("Không kiểm tra được QR vật tư. Hãy thử lại.");
                    return false;
                }

                const payload = await response.json();
                if (!payload?.found || !payload?.item) {
                    updateStatus(payload?.errorMessage || "Không tìm thấy vật tư còn tồn kho cho mã QR này.");
                    return false;
                }

                return addDetailItem(payload.item);
            } catch {
                updateStatus("Không kiểm tra được QR vật tư. Hãy thử lại.");
                return false;
            } finally {
                isLookupProcessing = false;
            }
        };

        const normalizeSearchItem = (item) => ({
            vatTuId: Number(item?.vatTuId || 0),
            hangHoaId: item?.hangHoaId == null ? "" : item.hangHoaId,
            tenChiTiet: `${item?.tenChiTiet || ""}`.trim(),
            tenHangHoa: `${item?.tenHangHoa || ""}`.trim(),
            maHangHoa: `${item?.maHangHoa || ""}`.trim(),
            tenKho: `${item?.tenKho || ""}`.trim(),
            maKho: `${item?.maKho || ""}`.trim(),
            donViTinh: `${item?.donViTinh || ""}`.trim(),
            qrCode: `${item?.qRCode ?? item?.qrCode ?? ""}`.trim(),
            soLuongTon: item?.soLuongTon,
            soLuongXuat: item?.soLuongXuat,
            maSoLo: `${item?.maSoLo || ""}`.trim(),
            viTriLuuKho: `${item?.viTriLuuKho || ""}`.trim(),
            imageUrl: `${item?.imageUrl || ""}`.trim()
        });

        const hideSearchResults = () => {
            if (searchResults instanceof HTMLElement) {
                searchResults.hidden = true;
                searchResults.replaceChildren();
            }
        };

        const setScannerCollapsed = (isCollapsed) => {
            if (!(scannerBody instanceof HTMLElement) || !(scannerToggle instanceof HTMLButtonElement)) {
                return;
            }

            scannerBody.hidden = isCollapsed;
            scannerToggle.setAttribute("aria-expanded", String(!isCollapsed));
            scannerToggle.setAttribute(
                "aria-label",
                isCollapsed ? "Mở rộng cụm tìm kiếm vật tư" : "Thu gọn cụm tìm kiếm vật tư");
            const icon = scannerToggle.querySelector("i");
            if (icon instanceof HTMLElement) {
                icon.classList.toggle("fa-chevron-down", isCollapsed);
                icon.classList.toggle("fa-chevron-up", !isCollapsed);
            }
        };

        const buildSearchMeta = (item) => {
            const segments = [];
            if (item.tenHangHoa) {
                segments.push(item.tenHangHoa);
            }
            if (item.maSoLo) {
                segments.push(`Lô: ${item.maSoLo}`);
            }
            if (item.tenKho) {
                segments.push(`Kho: ${item.tenKho}`);
            }
            if (item.viTriLuuKho) {
                segments.push(`Vị trí: ${item.viTriLuuKho}`);
            }
            return segments.join(" • ");
        };

        const renderSearchResults = (items, query) => {
            if (!(searchResults instanceof HTMLElement)) {
                return;
            }

            searchResults.replaceChildren();
            if (!query) {
                hideSearchResults();
                return;
            }

            if (items.length === 0) {
                searchResults.innerHTML = '<div class="xuat-kho-search-empty">Không tìm thấy vật tư còn tồn kho phù hợp.</div>';
                searchResults.hidden = false;
                return;
            }

            const fragment = document.createDocumentFragment();
            items.forEach((item) => {
                const button = document.createElement("button");
                button.type = "button";
                button.className = "xuat-kho-search-item";
                button.dataset.vatTuId = `${item.vatTuId}`;
                const imageMarkup = item.imageUrl
                    ? `<img src="${escapeHtml(item.imageUrl)}" alt="${escapeHtml(item.tenChiTiet)}" loading="lazy" />`
                    : '<span class="xuat-kho-search-image-placeholder"><i class="fa-solid fa-box-open" aria-hidden="true"></i></span>';
                button.innerHTML = `
                    <span class="xuat-kho-search-image">${imageMarkup}</span>
                    <span class="xuat-kho-search-body">
                        <strong>${escapeHtml(item.tenChiTiet || `Vật tư #${item.vatTuId}`)}</strong>
                        <span>${escapeHtml(buildSearchMeta(item))}</span>
                    </span>
                    <span class="xuat-kho-search-stock">Tồn ${escapeHtml(formatNumber(item.soLuongTon))}</span>
                `;
                button.addEventListener("click", () => {
                    const added = addDetailItem(item);
                    if (added && searchInput instanceof HTMLInputElement) {
                        searchInput.value = "";
                        hideSearchResults();
                        searchInput.focus();
                    }
                });
                fragment.appendChild(button);
            });

            searchResults.appendChild(fragment);
            searchResults.hidden = false;
        };

        const searchVatTuForExport = async (query) => {
            const normalizedQuery = `${query || ""}`.trim();
            const currentToken = ++searchRequestToken;
            if (normalizedQuery.length < 2) {
                hideSearchResults();
                return;
            }

            try {
                const response = await fetch(`/XuatKho/SearchVatTu?keyword=${encodeURIComponent(normalizedQuery)}`, {
                    method: "GET",
                    headers: {
                        "X-Requested-With": "XMLHttpRequest"
                    },
                    cache: "no-store"
                });

                const payload = await response.json().catch(() => null);
                if (currentToken !== searchRequestToken) {
                    return;
                }

                if (!response.ok || payload?.succeeded === false) {
                    renderSearchResults([], normalizedQuery);
                    updateStatus(payload?.errorMessage || "Không thể tìm vật tư. Hãy thử lại.");
                    return;
                }

                const items = Array.isArray(payload?.items)
                    ? payload.items.map(normalizeSearchItem).filter((item) => item.vatTuId > 0)
                    : [];
                renderSearchResults(items, normalizedQuery);
            } catch {
                if (currentToken === searchRequestToken) {
                    renderSearchResults([], normalizedQuery);
                    updateStatus("Không thể tìm vật tư. Hãy thử lại.");
                }
            }
        };

        const populateCameraOptions = (cameras) => {
            cameraOptions = cameras;
            if (!(cameraSelect instanceof HTMLSelectElement)) {
                return;
            }

            cameraSelect.innerHTML = "";
            cameraOptions.forEach((camera) => {
                const option = document.createElement("option");
                option.value = camera.id;
                option.textContent = camera.label || `Camera ${cameraSelect.options.length + 1}`;
                cameraSelect.appendChild(option);
            });

            if (cameraShell instanceof HTMLElement) {
                cameraShell.hidden = cameraOptions.length <= 1;
            }

            if (!selectedCameraId || !cameraOptions.some((camera) => camera.id === selectedCameraId)) {
                const preferredCamera = getPreferredQrCamera(cameraOptions);
                selectedCameraId = preferredCamera?.id || "";
                appendQrDebugLog("qr_preferred_camera", {
                    scope: "xuat-kho",
                    selectedCameraId,
                    label: getQrCameraLabel(selectedCameraId, cameraOptions)
                });
            }

            if (selectedCameraId) {
                cameraSelect.value = selectedCameraId;
                syncLiveSelectState(cameraSelect);
            }
        };

        const ensureCamerasLoaded = async () => {
            if (cameraOptions.length > 0) {
                return cameraOptions;
            }

            const cameras = typeof window.Html5Qrcode?.getCameras === "function"
                ? await window.Html5Qrcode.getCameras()
                : [];
            appendQrDebugLog("qr_cameras_loaded", {
                scope: "xuat-kho",
                cameras: summarizeQrCameras(cameras)
            });
            populateCameraOptions(cameras);
            return cameraOptions;
        };

        const setScannerMode = (mode) => {
            if (panel instanceof HTMLElement) {
                panel.hidden = mode !== "camera";
            }

            if (summary instanceof HTMLElement) {
                summary.classList.toggle("is-empty", mode !== "camera");
            }

            if (summaryDefault instanceof HTMLElement) {
                summaryDefault.classList.toggle("is-hidden", mode === "camera");
            }

            if (stopButton instanceof HTMLButtonElement) {
                stopButton.hidden = mode !== "camera" && mode !== "file";
            }
        };

        const stopScanner = async (message = "Đã dừng quét QR.") => {
            if (html5QrCode && isRunning) {
                try {
                    await html5QrCode.stop();
                } catch {
                }
            }

            isRunning = false;
            isFileProcessing = false;
            setScannerMode("idle");
            if (message) {
                updateStatus(message);
            }
        };

        getRows().forEach((row) => attachRowEvents(row));
        reindexRows();
        updateEmptyState();

        if (manualInput instanceof HTMLInputElement && addManualButton instanceof HTMLButtonElement) {
            addManualButton.addEventListener("click", async () => {
                const accepted = await lookupAndAddQr(manualInput.value);
                if (accepted) {
                    manualInput.value = "";
                    manualInput.focus();
                }
            });

            manualInput.addEventListener("keydown", (event) => {
                if (event.key === "Enter") {
                    event.preventDefault();
                    addManualButton.click();
                }
            });
        }

        if (searchInput instanceof HTMLInputElement) {
            searchInput.addEventListener("input", () => {
                window.clearTimeout(searchDebounceTimer);
                const query = searchInput.value;
                searchDebounceTimer = window.setTimeout(() => {
                    void searchVatTuForExport(query);
                }, 220);
            });

            searchInput.addEventListener("keydown", (event) => {
                if (event.key !== "Enter") {
                    return;
                }

                event.preventDefault();
                const firstResult = searchResults?.querySelector(".xuat-kho-search-item");
                if (firstResult instanceof HTMLButtonElement) {
                    firstResult.click();
                }
            });

            searchInput.addEventListener("blur", () => {
                window.setTimeout(hideSearchResults, 160);
            });

            searchInput.addEventListener("focus", () => {
                if (searchInput.value.trim().length >= 2 && searchResults instanceof HTMLElement && searchResults.children.length > 0) {
                    searchResults.hidden = false;
                }
            });
        }

        if (scannerToggle instanceof HTMLButtonElement && scannerBody instanceof HTMLElement) {
            scannerToggle.addEventListener("click", async () => {
                const willCollapse = !scannerBody.hidden;
                if (willCollapse) {
                    hideSearchResults();
                    if (isRunning || isFileProcessing) {
                        await stopScanner("");
                    }
                }
                setScannerCollapsed(willCollapse);
            });
        }

        if (!isReadonly &&
            startButton instanceof HTMLButtonElement &&
            stopButton instanceof HTMLButtonElement &&
            fileInput instanceof HTMLInputElement &&
            reader instanceof HTMLElement) {
            startButton.addEventListener("click", async () => {
                appendQrDebugLog("qr_click_start", {
                    scope: "xuat-kho",
                    isRunning,
                    isStarting,
                    isFileProcessing,
                    state: getQrScannerStateName(html5QrCode)
                });
                if (isRunning || isStarting) {
                    updateStatus("Camera đang quét QR.");
                    return;
                }

                isStarting = true;
                if (!window.isSecureContext && window.location.hostname !== "localhost" && window.location.hostname !== "127.0.0.1") {
                    updateStatus("Trình duyệt chỉ cho phép mở camera trên HTTPS hoặc localhost.");
                    isStarting = false;
                    return;
                }

                if (!navigator.mediaDevices?.getUserMedia) {
                    updateStatus("Thiết bị hiện tại không hỗ trợ camera trên trình duyệt này.");
                    isStarting = false;
                    return;
                }

                try {
                    await loadHtml5Qrcode();
                    if (!html5QrCode) {
                        html5QrCode = createHtml5QrCodeInstance(reader.id);
                    }

                    await resetQrScannerInstance(html5QrCode, "xuat-kho");
                    const cameras = await ensureCamerasLoaded();
                    if (!selectedCameraId && cameras.length > 0) {
                        selectedCameraId = cameras[0].id;
                    }

                    if (!selectedCameraId) {
                        updateStatus("Không tìm thấy camera để quét QR.");
                        isStarting = false;
                        return;
                    }

                    setScannerMode("camera");
                    updateStatus("Đưa mã QR vật tư vào giữa khung quét.");

                    await startQrScannerCamera(
                        html5QrCode,
                        reader,
                        selectedCameraId,
                        async (decodedText) => {
                            const accepted = await lookupAndAddQr(decodedText);
                            if (accepted) {
                                updateStatus("Đã nhận QR vật tư. Tiếp tục quét QR tiếp theo hoặc bấm Dừng.");
                            }
                        },
                        () => {
                        });
                    isRunning = true;
                } catch {
                    isRunning = false;
                    setScannerMode("idle");
                    updateStatus("Không thể truy cập camera hoặc khởi động bộ quét QR.");
                } finally {
                    isStarting = false;
                }
            });

            stopButton.addEventListener("click", async () => {
                if (isFileProcessing) {
                    fileScanToken += 1;
                    isFileProcessing = false;
                    setScannerMode("idle");
                    updateStatus("Đã dừng xử lý ảnh QR.");
                    return;
                }

                await stopScanner();
            });

            fileInput.addEventListener("change", async () => {
                const file = fileInput.files?.[0];
                if (!file) {
                    return;
                }

                try {
                    await loadHtml5Qrcode();
                    if (!html5QrCode) {
                        html5QrCode = createHtml5QrCodeInstance(reader.id);
                    }

                    if (isRunning) {
                        await stopScanner("");
                    }

                    isFileProcessing = true;
                    const currentFileToken = ++fileScanToken;
                    setScannerMode("file");
                    updateStatus("Đang đọc mã QR từ ảnh đã chọn...");

                    const decodedText = await scanQrFromImageFile(html5QrCode, file);
                    if (!isFileProcessing || currentFileToken !== fileScanToken) {
                        return;
                    }

                    isFileProcessing = false;
                    setScannerMode("idle");
                    await lookupAndAddQr(decodedText);
                } catch {
                    isFileProcessing = false;
                    setScannerMode("idle");
                    updateStatus("Không đọc được QR từ ảnh đã chọn. Hãy thử ảnh rõ hơn hoặc ảnh khác.");
                } finally {
                    fileInput.value = "";
                }
            });

            if (cameraSelect instanceof HTMLSelectElement) {
                cameraSelect.addEventListener("change", async () => {
                    selectedCameraId = cameraSelect.value;
                    appendQrDebugLog("qr_camera_select_change", {
                        scope: "xuat-kho",
                        selectedCameraId,
                        label: getQrCameraLabel(selectedCameraId, cameraOptions)
                    });
                    if (!isRunning || !selectedCameraId) {
                        return;
                    }

                    await stopScanner("Đang chuyển camera...");
                    startButton.click();
                });
            }
        }

        form.addEventListener("submit", (event) => {
            let hasError = false;
            const rows = getRows();
            if (rows.length === 0) {
                hasError = true;
                updateStatus("Vui lòng scan hoặc thêm ít nhất một vật tư cần xuất.");
            }

            rows.forEach((row) => {
                const quantityInput = row.querySelector("[data-detail-quantity]");
                if (quantityInput instanceof HTMLInputElement) {
                    clampQuantity(quantityInput);
                    const quantity = parseNumber(quantityInput.value);
                    const max = parseNumber(quantityInput.max);
                    if (quantity < 1 || (max > 0 && quantity > max)) {
                        hasError = true;
                    }
                }
            });

            if (hasError) {
                event.preventDefault();
                const tabGroup = root.closest("[data-tab-group]");
                if (tabGroup instanceof HTMLElement) {
                    activateTabInGroup(tabGroup, "vat-tu-xuat");
                }
            }
        });

        window.addEventListener("pagehide", () => {
            void stopScanner("");
        });
    })();
})();
