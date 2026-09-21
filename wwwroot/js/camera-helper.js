(() => {
    "use strict";

    const delay = (milliseconds) => new Promise((resolve) => window.setTimeout(resolve, milliseconds));

    const getSupportedConstraints = () => navigator.mediaDevices?.getSupportedConstraints?.() || {};

    const buildVideoConstraints = (deviceId) => {
        const supported = getSupportedConstraints();
        const constraints = {};

        if (deviceId && supported.deviceId) {
            constraints.deviceId = { exact: deviceId };
        } else if (supported.facingMode) {
            constraints.facingMode = { ideal: "environment" };
        }
        if (supported.width) {
            constraints.width = { ideal: 1280 };
        }
        if (supported.height) {
            constraints.height = { ideal: 1280 };
        }

        return Object.keys(constraints).length > 0 ? constraints : true;
    };

    const applyCameraConstraints = async (track) => {
        if (!track?.applyConstraints) {
            return false;
        }

        const supported = getSupportedConstraints();
        const capabilities = track.getCapabilities?.() || {};
        const advanced = {};
        const supportsValue = (name, value) => Array.isArray(capabilities[name]) && capabilities[name].includes(value);

        if (supported.focusMode && supportsValue("focusMode", "continuous")) {
            advanced.focusMode = "continuous";
        }
        if (supported.exposureMode && supportsValue("exposureMode", "continuous")) {
            advanced.exposureMode = "continuous";
        }
        if (supported.whiteBalanceMode && supportsValue("whiteBalanceMode", "continuous")) {
            advanced.whiteBalanceMode = "continuous";
        }

        if (Object.keys(advanced).length === 0) {
            return false;
        }

        try {
            await track.applyConstraints({ advanced: [advanced] });
            return true;
        } catch {
            return false;
        }
    };

    const waitForCameraReady = async (video, warmupMilliseconds = 1200) => {
        const timeoutAt = Date.now() + 6000;
        while (video.readyState < HTMLMediaElement.HAVE_ENOUGH_DATA || video.videoWidth <= 0 || video.videoHeight <= 0) {
            if (Date.now() >= timeoutAt) {
                throw new Error("Camera chưa sẵn sàng. Vui lòng thử lại.");
            }
            await delay(80);
        }
        await delay(Math.max(1000, Math.min(1500, warmupMilliseconds)));
    };

    const openCamera = async (video, deviceId, warmupMilliseconds = 1200) => {
        if (!(video instanceof HTMLVideoElement) || !navigator.mediaDevices?.getUserMedia) {
            throw new Error("Trình duyệt không hỗ trợ camera.");
        }

        const preferredConstraints = buildVideoConstraints(deviceId);
        let stream;
        try {
            stream = await navigator.mediaDevices.getUserMedia({ video: preferredConstraints, audio: false });
        } catch (error) {
            if (deviceId) {
                stream = await navigator.mediaDevices.getUserMedia({
                    video: { facingMode: { ideal: "environment" } },
                    audio: false
                });
            } else {
                throw error;
            }
        }

        video.srcObject = stream;
        video.hidden = false;
        await video.play();
        await applyCameraConstraints(stream.getVideoTracks?.()[0] || null);
        await waitForCameraReady(video, warmupMilliseconds);
        return stream;
    };

    const loadBlobImage = async (blob) => {
        if (typeof createImageBitmap === "function") {
            return createImageBitmap(blob);
        }

        return new Promise((resolve, reject) => {
            const url = URL.createObjectURL(blob);
            const image = new Image();
            image.onload = () => {
                URL.revokeObjectURL(url);
                resolve(image);
            };
            image.onerror = () => {
                URL.revokeObjectURL(url);
                reject(new Error("Không thể đọc ảnh vừa chụp."));
            };
            image.src = url;
        });
    };

    const analyzeBrightness = async (blob) => {
        if (!(blob instanceof Blob) || blob.size === 0) {
            return { isUnderexposed: true, reason: "empty" };
        }

        const source = await loadBlobImage(blob);
        try {
            const sourceWidth = source.width || source.naturalWidth || 1;
            const sourceHeight = source.height || source.naturalHeight || 1;
            const width = Math.min(160, sourceWidth);
            const height = Math.max(1, Math.round(sourceHeight * (width / sourceWidth)));
            const canvas = document.createElement("canvas");
            canvas.width = width;
            canvas.height = height;
            const context = canvas.getContext("2d", { willReadFrequently: true });
            if (!context) {
                return { isUnderexposed: false, reason: "unavailable" };
            }

            context.drawImage(source, 0, 0, width, height);
            const pixels = context.getImageData(0, 0, width, height).data;
            const centerLeft = Math.floor(width * 0.25);
            const centerRight = Math.ceil(width * 0.75);
            const centerTop = Math.floor(height * 0.2);
            const centerBottom = Math.ceil(height * 0.8);
            let total = 0;
            let dark = 0;
            let centerTotal = 0;
            let centerDark = 0;
            let centerCount = 0;
            let outerTotal = 0;
            let outerCount = 0;

            for (let y = 0; y < height; y++) {
                for (let x = 0; x < width; x++) {
                    const index = (y * width + x) * 4;
                    const luminance = 0.2126 * pixels[index] + 0.7152 * pixels[index + 1] + 0.0722 * pixels[index + 2];
                    const isCenter = x >= centerLeft && x < centerRight && y >= centerTop && y < centerBottom;
                    total += luminance;
                    if (luminance < 55) dark++;
                    if (isCenter) {
                        centerTotal += luminance;
                        centerCount++;
                        if (luminance < 55) centerDark++;
                    } else {
                        outerTotal += luminance;
                        outerCount++;
                    }
                }
            }

            const pixelCount = width * height;
            const average = total / pixelCount;
            const centerAverage = centerTotal / Math.max(1, centerCount);
            const outerAverage = outerTotal / Math.max(1, outerCount);
            const darkRatio = dark / pixelCount;
            const centerDarkRatio = centerDark / Math.max(1, centerCount);
            const isVeryDark = average < 38 && darkRatio > 0.72;
            const isCenterDark = centerAverage < 48 && centerDarkRatio > 0.7;
            const isBacklit = centerAverage < 65 && outerAverage - centerAverage > 55 && centerDarkRatio > 0.6;

            return {
                isUnderexposed: isVeryDark || isCenterDark || isBacklit,
                reason: isBacklit ? "backlit" : isCenterDark ? "center-dark" : isVeryDark ? "dark" : "ok",
                average: Math.round(average * 10) / 10,
                centerAverage: Math.round(centerAverage * 10) / 10,
                outerAverage: Math.round(outerAverage * 10) / 10,
                darkRatio: Math.round(darkRatio * 1000) / 1000,
                centerDarkRatio: Math.round(centerDarkRatio * 1000) / 1000
            };
        } finally {
            source.close?.();
        }
    };

    window.ApptechCamera = Object.freeze({
        getSupportedConstraints,
        buildVideoConstraints,
        applyCameraConstraints,
        waitForCameraReady,
        openCamera,
        analyzeBrightness
    });
})();
