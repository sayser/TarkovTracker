let stage = document.getElementById('stage');
let content = document.getElementById('content');
let playerMarker = document.getElementById('playerMarker');
let markerLayer = document.getElementById('markerLayer');
let customMarkerLayer = document.getElementById('customMarkerLayer');
let hazardOutlineLayer = document.getElementById('hazardOutlineLayer');
let svg = content.querySelector('svg');

let scale = 1;
let panX = 0;
let panY = 0;
let isPanning = false;
let lastX = 0;
let lastY = 0;
let playerMarkerData = null;
let customPinsEnabled = false;
let customPins = [];
let customPinCounter = 0;

function getViewBox() {
    let raw = svg.getAttribute('viewBox');
    if (!raw) return { x: 0, y: 0, w: 1000, h: 1000 };

    let vb = raw.trim().split(/\s+/).map(Number);
    return { x: vb[0], y: vb[1], w: vb[2], h: vb[3] };
}

function initialize() {
    let vb = getViewBox();

    svg.setAttribute('width', vb.w);
    svg.setAttribute('height', vb.h);

    content.style.width = vb.w + 'px';
    content.style.height = vb.h + 'px';

    if (hazardOutlineLayer) {
        hazardOutlineLayer.setAttribute('width', vb.w);
        hazardOutlineLayer.setAttribute('height', vb.h);
        hazardOutlineLayer.setAttribute('viewBox', `0 0 ${vb.w} ${vb.h}`);
    }

    resetView();
}

function applyTransform() {
    content.style.transform = `translate(${panX}px, ${panY}px) scale(${scale})`;
    updatePlayerMarkerVisual();
    updateMapMarkerVisuals();
    updateCustomMarkerVisuals();
}

function resetView() {
    let vb = getViewBox();

    let sx = stage.clientWidth / vb.w;
    let sy = stage.clientHeight / vb.h;

    scale = Math.min(sx, sy) * 0.95;

    panX = (stage.clientWidth - vb.w * scale) / 2;
    panY = (stage.clientHeight - vb.h * scale) / 2;

    applyTransform();
}

function setPlayerMarkerNormalized(nx, ny, directionDegrees) {
    let vb = getViewBox();

    playerMarkerData = {
        x: nx * vb.w,
        y: ny * vb.h,
        direction: directionDegrees
    };

    updatePlayerMarkerVisual();
}

function updatePlayerMarkerVisual() {
    if (!playerMarkerData) return;

    let inverseScale = 1 / scale;

    playerMarker.style.left = playerMarkerData.x + 'px';
    playerMarker.style.top = playerMarkerData.y + 'px';
    playerMarker.style.display = 'block';

    playerMarker.style.transform =
        `rotate(${playerMarkerData.direction + 180}deg) scale(${inverseScale})`;
}

function clearMapMarkers() {
    markerLayer.innerHTML = '';
    if (hazardOutlineLayer) {
        hazardOutlineLayer.innerHTML = '';
    }
}

function addMapMarkers(markers) {
    clearMapMarkers();

    let vb = getViewBox();

    for (let m of markers) {
        let marker = document.createElement('div');

        marker.className = 'mapMarker ' + m.cssClass;
        marker.dataset.markerType = m.markerType;
        marker.dataset.markerName = m.name || '';
        marker.dataset.faction = m.faction || '';
        marker.dataset.questCategory = m.questCategory || '';
        if (m.gameY != null && !Number.isNaN(m.gameY)) {
            marker.dataset.gameY = String(m.gameY);
        }

        marker.title = m.tooltip;

        let x = m.normalizedX * vb.w;
        let y = m.normalizedY * vb.h;

        marker.style.left = x + 'px';
        marker.style.top = y + 'px';

        let dot = document.createElement('div');
        dot.className = 'markerDot';

        if (m.markerType === 'quest' && m.questCategory === 'item' && m.questItemIconLink) {
            dot.classList.add('markerItemIcon');
            dot.style.backgroundImage = "url('" + m.questItemIconLink.replace(/'/g, '%27') + "')";
        }

        marker.appendChild(dot);

        if (
            m.markerType !== 'spawn-pmc' &&
            m.markerType !== 'spawn-scav' &&
            m.markerType !== 'spawn-boss' &&
            m.markerType !== 'spawn-cultist' &&
            !m.hideLabel
        ) {
            let label = document.createElement('div');
            label.className = 'markerLabel';
            label.textContent = m.name;

            if (m.markerType === 'label') {
                label.style.fontSize = (m.labelSize || 8) + 'px';
                label.style.transform = 'translate(-50%, -50%) rotate(' + (m.labelRotation || 0) + 'deg)';
            }

            marker.appendChild(label);
        }

        marker.addEventListener('click', function(e) {
            e.stopPropagation();

            if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage({
                    messageType: 'markerClicked',
                    name: m.name,
                    markerType: m.markerType,
                    faction: m.faction || '',
                    zoneName: m.zoneName || '',
                    categories: m.categories || '',
                    conditions: m.conditions || '',
                    position: m.position || ''
                });
            }
        });

        markerLayer.appendChild(marker);
    }

    updateMapMarkerVisuals();
    refreshMarkerLevelVisibility();
    refreshRaidExfilHighlights();
}

function updateMapMarkerVisuals() {
    let inverseScale = 1 / scale;

    document.querySelectorAll('#markerLayer .mapMarker').forEach(function(marker) {
        marker.style.transform = `scale(${inverseScale})`;
    });
}

function updateCustomMarkerVisuals() {
    if (!customMarkerLayer) return;

    let inverseScale = 1 / scale;

    customMarkerLayer.querySelectorAll('.custom-pin').forEach(function(marker) {
        marker.style.transform = `scale(${inverseScale})`;
    });
}

function notifyCustomPinsChanged() {
    if (!window.chrome || !window.chrome.webview) return;

    window.chrome.webview.postMessage({
        messageType: 'customPinsChanged',
        pins: customPins.map(function(pin) {
            return {
                id: pin.id,
                normalizedX: pin.normalizedX,
                normalizedY: pin.normalizedY
            };
        })
    });
}

function clearCustomPins() {
    customPins = [];
    if (customMarkerLayer) {
        customMarkerLayer.innerHTML = '';
    }
}

function setCustomPinsMode(enabled) {
    customPinsEnabled = !!enabled;
    stage.classList.toggle('custom-pins-active', customPinsEnabled);

    if (!customPinsEnabled) {
        clearCustomPins();
        notifyCustomPinsChanged();
    }
}

function setCustomPins(pins) {
    clearCustomPins();

    for (let pin of pins || []) {
        if (pin == null || pin.normalizedX == null || pin.normalizedY == null) continue;

        let entry = {
            id: pin.id || ('pin-' + (++customPinCounter)),
            normalizedX: pin.normalizedX,
            normalizedY: pin.normalizedY
        };

        customPins.push(entry);
        renderCustomPin(entry);
    }

    updateCustomMarkerVisuals();
}

function screenToNormalizedMap(clientX, clientY) {
    let rect = stage.getBoundingClientRect();
    let stageX = clientX - rect.left;
    let stageY = clientY - rect.top;
    let vb = getViewBox();
    let mapX = (stageX - panX) / scale;
    let mapY = (stageY - panY) / scale;

    return {
        normalizedX: mapX / vb.w,
        normalizedY: mapY / vb.h
    };
}

function addCustomPinAtNormalized(normalizedX, normalizedY) {
    let pin = {
        id: 'pin-' + (++customPinCounter),
        normalizedX: normalizedX,
        normalizedY: normalizedY
    };

    customPins.push(pin);
    renderCustomPin(pin);
    updateCustomMarkerVisuals();
    notifyCustomPinsChanged();
}

function removeCustomPinById(pinId) {
    customPins = customPins.filter(function(pin) {
        return pin.id !== pinId;
    });

    if (!customMarkerLayer) return;

    let marker = customMarkerLayer.querySelector('[data-pin-id="' + pinId + '"]');
    if (marker) marker.remove();

    notifyCustomPinsChanged();
}

function renderCustomPin(pin) {
    if (!customMarkerLayer) return;

    let vb = getViewBox();
    let marker = document.createElement('div');

    marker.className = 'custom-pin';
    marker.dataset.pinId = pin.id;
    marker.dataset.markerType = 'custom-pin';
    marker.title = 'Custom pin — left-click to remove';
    marker.style.left = (pin.normalizedX * vb.w) + 'px';
    marker.style.top = (pin.normalizedY * vb.h) + 'px';

    let dot = document.createElement('div');
    dot.className = 'customPinDot';
    marker.appendChild(dot);

    marker.addEventListener('click', function(e) {
        e.stopPropagation();
        removeCustomPinById(pin.id);
    });

    customMarkerLayer.appendChild(marker);
}

function setExtractFactionVisibility(faction, visible) {
    document.querySelectorAll('.mapMarker[data-marker-type="extract"][data-faction="' + faction + '"]').forEach(function(marker) {
        marker.style.display = visible ? 'block' : 'none';
    });
}

function setTransitVisibility(visible) {
    document.querySelectorAll('.mapMarker[data-marker-type="transit"]').forEach(function(marker) {
        marker.style.display = visible ? 'block' : 'none';
    });
}

function setSpawnVisibility(spawnType, visible) {
    document.querySelectorAll('.mapMarker[data-marker-type="' + spawnType + '"]').forEach(function(marker) {
        marker.style.display = visible ? 'block' : 'none';
    });
}

function setLabelVisibility(visible) {
    document.querySelectorAll('.mapMarker[data-marker-type="label"]').forEach(function(marker) {
        marker.style.display = visible ? 'block' : 'none';
    });
}

function setQuestVisibility(visible) {
    document.querySelectorAll('.mapMarker[data-marker-type="quest"]').forEach(function(marker) {
        marker.style.display = visible ? 'block' : 'none';
    });
}

function setQuestCategoryVisibility(category, visible) {
    document.querySelectorAll('.mapMarker[data-marker-type="quest"][data-quest-category="' + category + '"]').forEach(function(marker) {
        marker.style.display = visible ? 'block' : 'none';
    });
}

function setHazardVisibility(visible) {
    document.querySelectorAll('.mapMarker[data-marker-type="hazard"]').forEach(function(marker) {
        marker.style.display = visible ? 'block' : 'none';
    });

    if (hazardOutlineLayer) {
        hazardOutlineLayer.querySelectorAll('polygon[data-marker-type="hazard"]').forEach(function(polygon) {
            polygon.style.display = visible ? '' : 'none';
        });
    }
}

function setSwitchVisibility(visible) {
    document.querySelectorAll('.mapMarker[data-marker-type="switch"]').forEach(function(marker) {
        marker.style.display = visible ? 'block' : 'none';
    });
}

function setBtrStopVisibility(visible) {
    document.querySelectorAll('.mapMarker[data-marker-type="btr-stop"]').forEach(function(marker) {
        marker.style.display = visible ? 'block' : 'none';
    });
}

function applyMarkerFilters(filters) {
    if (!filters) return;

    setExtractFactionVisibility('pmc', !!filters.pmcExtracts);
    setExtractFactionVisibility('scav', !!filters.scavExtracts);
    setExtractFactionVisibility('shared', !!filters.sharedExtracts);
    setTransitVisibility(!!filters.transits);
    setSpawnVisibility('spawn-pmc', !!filters.pmcSpawns);
    setSpawnVisibility('spawn-scav', !!filters.scavSpawns);
    setSpawnVisibility('spawn-boss', !!filters.bossSpawns);
    setSpawnVisibility('spawn-cultist', !!filters.cultistSpawns);
    setLabelVisibility(!!filters.labels);
    setQuestCategoryVisibility('item', !!filters.questItems);
    setQuestCategoryVisibility('objective', !!filters.questObjectives);
    setHazardVisibility(!!filters.hazards);
    setSwitchVisibility(!!filters.switches);
    setBtrStopVisibility(!!filters.btrStops);
    setCustomPinsMode(!!filters.customPins);
    refreshRaidExfilHighlights();
}

let raidExfilState = {
    active: false,
    extractNames: [],
    transitNames: []
};

function normalizeRaidName(name) {
    return (name || '').toLowerCase().replace(/[^a-z0-9]/g, '');
}

function setRaidExfilHighlights(state) {
    if (!state) {
        raidExfilState = { active: false, extractNames: [], transitNames: [] };
    } else {
        raidExfilState = {
            active: state.active === true,
            extractNames: state.extractNames || [],
            transitNames: state.transitNames || []
        };
    }

    refreshRaidExfilHighlights();
}

function refreshRaidExfilHighlights() {
    let extractSet = new Set((raidExfilState.extractNames || []).map(normalizeRaidName));
    let transitSet = new Set((raidExfilState.transitNames || []).map(normalizeRaidName));
    let active = raidExfilState.active === true;

    document.querySelectorAll('.mapMarker').forEach(function(marker) {
        marker.classList.remove('raid-available', 'raid-dimmed');

        if (!active)
            return;

        let markerType = marker.dataset.markerType;
        let markerName = normalizeRaidName(marker.dataset.markerName);

        if (markerType === 'extract' && extractSet.has(markerName)) {
            marker.classList.add('raid-available');
            marker.style.display = 'block';
        } else if (markerType === 'transit' && transitSet.has(markerName)) {
            marker.classList.add('raid-available');
            marker.style.display = 'block';
        } else if (markerType === 'extract' || markerType === 'transit') {
            marker.classList.add('raid-dimmed');
        }
    });
}

let mapLevelState = {
    defaultLayerId: null,
    overlayLayerIds: [],
    showBaseLayer: true,
    dimBase: false,
    activeLevelIds: [],
    levelExtents: []
};

function refreshMarkerLevelVisibility() {
    const extents = mapLevelState.levelExtents || [];
    if (extents.length === 0) {
        document.querySelectorAll('.mapMarker.off-level').forEach(function(marker) {
            marker.classList.remove('off-level');
        });
        return;
    }

    const activeIds = new Set(mapLevelState.activeLevelIds || []);

    document.querySelectorAll('.mapMarker[data-game-y]').forEach(function(marker) {
        const y = parseFloat(marker.dataset.gameY);
        if (Number.isNaN(y)) {
            marker.classList.remove('off-level');
            return;
        }

        let onLevel = true;
        for (const ext of extents) {
            if (y >= ext.minHeight && y < ext.maxHeight) {
                onLevel = activeIds.has(ext.svgLayer);
                break;
            }
        }

        marker.classList.toggle('off-level', !onLevel);
    });
}

function setupMapLayerClasses(defaultLayerId, overlayLayerIds) {
    const svgRoot = document.querySelector('#content svg');
    if (!svgRoot || !defaultLayerId) return;

    for (const child of svgRoot.children) {
        if (child.tagName !== 'g' || !child.id) continue;

        child.classList.remove('base-layer', 'overlay-layer', 'hidden-layer', 'force-hidden');

        const keepWithBase = child.dataset && child.dataset.keepWithGroup === defaultLayerId;
        if (child.id === defaultLayerId || keepWithBase) {
            child.classList.add('base-layer');
        } else {
            child.classList.add('overlay-layer', 'hidden-layer');
        }
    }
}

function refreshMapLevelDisplay() {
    const svgRoot = document.querySelector('#content svg');
    if (!svgRoot || !mapLevelState.defaultLayerId) return;

    const activeSet = new Set(mapLevelState.activeLevelIds || []);
    const hasActiveOverlay = activeSet.size > 0;

    if (mapLevelState.showBaseLayer && mapLevelState.dimBase && hasActiveOverlay) {
        svgRoot.classList.add('off-level');
    } else {
        svgRoot.classList.remove('off-level');
    }

    for (const child of svgRoot.children) {
        if (child.tagName !== 'g' || !child.id) continue;

        if (child.classList.contains('base-layer')) {
            if (!mapLevelState.showBaseLayer) {
                child.classList.add('force-hidden');
            } else {
                child.classList.remove('force-hidden');
            }
            continue;
        }

        if (child.classList.contains('overlay-layer')) {
            if (activeSet.has(child.id)) {
                child.classList.remove('hidden-layer');
            } else {
                child.classList.add('hidden-layer');
            }
        }
    }
}

function applyMapLevelState(state) {
    if (!state || !state.defaultLayerId) return;

    const overlayLayerIds = state.overlayLayerIds || [];
    const needsSetup =
        mapLevelState.defaultLayerId !== state.defaultLayerId ||
        JSON.stringify(mapLevelState.overlayLayerIds) !== JSON.stringify(overlayLayerIds);

    if (needsSetup) {
        setupMapLayerClasses(state.defaultLayerId, overlayLayerIds);
    }

    mapLevelState.defaultLayerId = state.defaultLayerId;
    mapLevelState.overlayLayerIds = overlayLayerIds;
    mapLevelState.showBaseLayer = state.showBaseLayer !== false;
    mapLevelState.dimBase = state.dimBase === true;
    mapLevelState.activeLevelIds = state.activeLevelIds || [];
    mapLevelState.levelExtents = state.levelExtents || [];

    refreshMapLevelDisplay();
    refreshMarkerLevelVisibility();
}

stage.addEventListener('wheel', function(e) {
    e.preventDefault();

    let oldScale = scale;
    let zoomFactor = e.deltaY < 0 ? 1.15 : 1 / 1.15;
    let newScale = Math.max(0.2, Math.min(oldScale * zoomFactor, 12));

    let rect = stage.getBoundingClientRect();
    let mx = e.clientX - rect.left;
    let my = e.clientY - rect.top;

    let ratio = newScale / oldScale;

    panX = mx - (mx - panX) * ratio;
    panY = my - (my - panY) * ratio;

    scale = newScale;
    applyTransform();
});

stage.addEventListener('mousedown', function(e) {
    if (e.button !== 0) return;

    isPanning = true;
    lastX = e.clientX;
    lastY = e.clientY;
    stage.style.cursor = 'grabbing';
});

stage.addEventListener('contextmenu', function(e) {
    e.preventDefault();

    if (!customPinsEnabled) return;

    let coords = screenToNormalizedMap(e.clientX, e.clientY);
    if (coords.normalizedX < 0 || coords.normalizedX > 1 ||
        coords.normalizedY < 0 || coords.normalizedY > 1) {
        return;
    }

    addCustomPinAtNormalized(coords.normalizedX, coords.normalizedY);
});

window.addEventListener('mouseup', function() {
    isPanning = false;
    stage.style.cursor = 'grab';
});

window.addEventListener('mousemove', function(e) {
    if (!isPanning) return;

    panX += e.clientX - lastX;
    panY += e.clientY - lastY;

    lastX = e.clientX;
    lastY = e.clientY;

    applyTransform();
});

window.addEventListener('resize', resetView);

initialize();
