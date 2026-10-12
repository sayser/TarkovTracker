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
let cachedMapMarkers = [];
let cachedCustomPins = [];

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
}

function refreshCounterScales() {
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
    refreshCounterScales();
}

function centerMapOnPlayer() {
    if (!playerMarkerData) return;

    let stageWidth = stage.clientWidth;
    let stageHeight = stage.clientHeight;

    panX = (stageWidth / 2) - (playerMarkerData.x * scale);
    panY = (stageHeight / 2) - (playerMarkerData.y * scale);

    applyTransform();
}

function setPlayerMarkerNormalized(nx, ny, directionDegrees, centerOnPlayer) {
    let vb = getViewBox();

    playerMarkerData = {
        x: nx * vb.w,
        y: ny * vb.h,
        direction: directionDegrees
    };

    updatePlayerMarkerVisual();

    if (centerOnPlayer) {
        centerMapOnPlayer();
    }
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
    cachedMapMarkers = [];
    markerLayer.innerHTML = '';
    if (hazardOutlineLayer) {
        hazardOutlineLayer.innerHTML = '';
    }
}

function sanitizeCssClass(value) {
    return String(value || '').replace(/[^a-zA-Z0-9_-]+/g, ' ').trim();
}

function isSafeHttpUrl(value) {
    try {
        let url = new URL(value);
        return url.protocol === 'https:';
    } catch (e) {
        return false;
    }
}

function buildMarkerSearchText(m) {
    return [
        m.name,
        m.markerType,
        m.faction,
        m.questCategory,
        m.questTrader,
        m.questItem,
        m.questItemShortName,
        m.categories,
        m.zoneName,
        m.conditions,
        m.keys,
        Array.isArray(m.requirements) ? m.requirements.join(' ') : '',
        Array.isArray(m.extractSwitches) ? m.extractSwitches.join(' ') : ''
    ].filter(Boolean).join(' ').toLowerCase();
}

function addHazardZonePolygon(outline, hazardType, hazardName) {
    if (!hazardOutlineLayer || !outline || outline.length < 3) return;

    let vb = getViewBox();
    let points = outline
        .map(function(p) { return (p.normalizedX * vb.w) + ',' + (p.normalizedY * vb.h); })
        .join(' ');

    let polygon = document.createElementNS('http://www.w3.org/2000/svg', 'polygon');
    polygon.setAttribute('points', points);
    polygon.classList.add('hazardOutline');
    if (hazardType === 'minefield') {
        polygon.classList.add('hazardOutline-minefield');
    } else if (hazardType === 'sniper') {
        polygon.classList.add('hazardOutline-sniper');
    }
    polygon.dataset.markerType = 'hazard-zone';
    polygon.dataset.markerName = hazardName || '';
    polygon.dataset.searchText = (hazardName + ' hazard ' + (hazardType || '')).toLowerCase();
    hazardOutlineLayer.appendChild(polygon);
}

function addMapMarkers(markers) {
    clearMapMarkers();

    let vb = getViewBox();

    for (let m of markers) {
        let marker = document.createElement('div');

        marker.className = ('mapMarker ' + sanitizeCssClass(m.cssClass)).trim();
        marker.dataset.markerType = m.markerType;
        marker.dataset.markerName = m.name || '';
        marker.dataset.faction = m.faction || '';
        marker.dataset.questCategory = m.questCategory || '';
        if (m.markerType === 'quest') {
            marker.dataset.questName = m.name || '';
            marker.dataset.questTrader = m.questTrader || '';
        }
        if (m.markerType === 'switch' && m.switchId) {
            marker.dataset.switchId = m.switchId;
        }
        if (m.markerType === 'extract') {
            if (m.extractId) {
                marker.dataset.extractId = m.extractId;
            }
            if (m.linkedSwitchIds && m.linkedSwitchIds.length) {
                marker.dataset.linkedSwitchIds = m.linkedSwitchIds.join(',');
            }
        }
        marker.dataset.searchText = buildMarkerSearchText(m);
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

        if (m.markerType === 'quest' && m.questCategory === 'item' && isSafeHttpUrl(m.questItemIconLink)) {
            dot.classList.add('markerItemIcon');
            dot.style.backgroundImage = 'url(' + JSON.stringify(m.questItemIconLink) + ')';
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

            if (m.markerType === 'extract') {
                if (m.linkedSwitchIds && m.linkedSwitchIds.length) {
                    highlightLinkedSwitches(m.linkedSwitchIds);
                } else {
                    clearLinkedSwitchHighlights();
                }
            }

            if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage({
                    messageType: 'markerClicked',
                    name: m.name,
                    markerType: m.markerType,
                    faction: m.faction || '',
                    zoneName: m.zoneName || '',
                    categories: m.categories || '',
                    conditions: m.conditions || '',
                    keys: m.keys || '',
                    requirements: Array.isArray(m.requirements) ? m.requirements : [],
                    extractSwitches: Array.isArray(m.extractSwitches) ? m.extractSwitches : [],
                    wikiUrl: m.wikiUrl || '',
                    tarkovDevUrl: m.tarkovDevUrl || '',
                    position: m.position || '',
                    questSlug: m.questSlug || '',
                    linkedSwitchIds: m.linkedSwitchIds || []
                });
            }
        });

        markerLayer.appendChild(marker);

        if (m.outline && m.outline.length >= 3) {
            addHazardZonePolygon(m.outline, m.categories || '', m.name || '');
        }
    }

    cacheMapMarkers();
    updateMapMarkerVisuals();
    refreshMarkerLevelVisibility();
    refreshRaidExfilHighlights();
    refreshMarkerSearchHighlight();
    refreshQuestFilterVisibility();
}

function cacheMapMarkers() {
    cachedMapMarkers = markerLayer ? Array.prototype.slice.call(markerLayer.children) : [];
}

function cacheCustomPins() {
    cachedCustomPins = customMarkerLayer ? Array.prototype.slice.call(customMarkerLayer.children) : [];
}

function updateMapMarkerVisuals() {
    let inverseScale = 1 / scale;
    for (let i = 0; i < cachedMapMarkers.length; i++) {
        cachedMapMarkers[i].style.transform = `scale(${inverseScale})`;
    }
}

function updateCustomMarkerVisuals() {
    if (!customMarkerLayer) return;

    let inverseScale = 1 / scale;
    for (let i = 0; i < cachedCustomPins.length; i++) {
        cachedCustomPins[i].style.transform = `scale(${inverseScale})`;
    }
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
    cachedCustomPins = [];
    if (customMarkerLayer) {
        customMarkerLayer.innerHTML = '';
    }
}

function setCustomPinsVisibility(visible) {
    let display = visible ? 'block' : 'none';
    for (let i = 0; i < cachedCustomPins.length; i++) {
        cachedCustomPins[i].style.display = display;
    }
}

function setCustomPinsMode(enabled) {
    customPinsEnabled = !!enabled;
    stage.classList.toggle('custom-pins-active', customPinsEnabled);
    setCustomPinsVisibility(!!enabled);
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

        let idMatch = /^pin-(\d+)$/.exec(entry.id);
        if (idMatch) {
            customPinCounter = Math.max(customPinCounter, parseInt(idMatch[1], 10));
        }

        customPins.push(entry);
        renderCustomPin(entry);
    }

    cacheCustomPins();
    updateCustomMarkerVisuals();
    setCustomPinsVisibility(customPinsEnabled);
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
    cacheCustomPins();
    updateCustomMarkerVisuals();
    notifyCustomPinsChanged();
}

function removeCustomPinById(pinId) {
    customPins = customPins.filter(function(pin) {
        return pin.id !== pinId;
    });

    for (let i = cachedCustomPins.length - 1; i >= 0; i--) {
        if (cachedCustomPins[i].dataset.pinId === pinId) {
            cachedCustomPins[i].remove();
            cachedCustomPins.splice(i, 1);
        }
    }

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
    let display = visible ? 'block' : 'none';
    for (let i = 0; i < cachedMapMarkers.length; i++) {
        let marker = cachedMapMarkers[i];
        if (marker.dataset.markerType === 'extract' && marker.dataset.faction === faction)
            marker.style.display = display;
    }
}

function setTransitVisibility(visible) {
    setMarkerTypeVisibility('transit', visible);
}

function setSpawnVisibility(spawnType, visible) {
    setMarkerTypeVisibility(spawnType, visible);
}

function setLabelVisibility(visible) {
    setMarkerTypeVisibility('label', visible);
}

function setQuestVisibility(visible) {
    setMarkerTypeVisibility('quest', visible);
}

function setMarkerTypeVisibility(markerType, visible) {
    let display = visible ? 'block' : 'none';
    for (let i = 0; i < cachedMapMarkers.length; i++) {
        if (cachedMapMarkers[i].dataset.markerType === markerType)
            cachedMapMarkers[i].style.display = display;
    }
}

function setQuestCategoryVisibility(category, visible) {
    let display = visible ? 'block' : 'none';
    for (let i = 0; i < cachedMapMarkers.length; i++) {
        let marker = cachedMapMarkers[i];
        if (marker.dataset.markerType === 'quest' && marker.dataset.questCategory === category)
            marker.style.display = display;
    }
}

function setQuestNamesVisibility(visible) {
    let display = visible ? 'block' : 'none';
    for (let i = 0; i < cachedMapMarkers.length; i++) {
        let marker = cachedMapMarkers[i];
        if (marker.dataset.markerType !== 'quest') continue;
        let label = marker.querySelector('.markerLabel');
        if (label) label.style.display = display;
    }
}

function setHazardVisibility(visible) {
    setMarkerTypeVisibility('hazard', visible);
}

function setHazardZoneVisibility(visible) {
    if (!hazardOutlineLayer) return;

    hazardOutlineLayer.querySelectorAll('polygon[data-marker-type="hazard-zone"]').forEach(function(polygon) {
        polygon.style.display = visible ? '' : 'none';
    });
}

function setSwitchVisibility(visible) {
    setMarkerTypeVisibility('switch', visible);
}

function setBtrStopVisibility(visible) {
    setMarkerTypeVisibility('btr-stop', visible);
}

let markerSearchQuery = '';
let questFilterState = { questNames: [], trader: '' };

function applyMarkerSearch(query) {
    markerSearchQuery = (query || '').trim();
    refreshMarkerSearchHighlight();
}

function refreshMarkerSearchHighlight() {
    let terms = markerSearchQuery
        ? markerSearchQuery.toLowerCase().split(',').map(function(s) { return s.trim(); }).filter(Boolean)
        : [];

    for (let i = 0; i < cachedMapMarkers.length; i++) {
        let marker = cachedMapMarkers[i];
        marker.classList.remove('search-dimmed', 'search-match');
        if (terms.length === 0) continue;

        let haystack = marker.dataset.searchText || '';
        let matched = terms.some(function(term) { return haystack.indexOf(term) >= 0; });
        marker.classList.toggle('search-match', matched);
        marker.classList.toggle('search-dimmed', !matched);
    }

    if (hazardOutlineLayer) {
        hazardOutlineLayer.querySelectorAll('polygon[data-marker-type="hazard-zone"]').forEach(function(polygon) {
            polygon.classList.remove('search-dimmed', 'search-match');
            if (terms.length === 0) return;

            let haystack = polygon.dataset.searchText || '';
            let matched = terms.some(function(term) { return haystack.indexOf(term) >= 0; });
            polygon.classList.toggle('search-match', matched);
            polygon.classList.toggle('search-dimmed', !matched);
        });
    }
}

function applyQuestFilters(filters) {
    let questNames = [];

    if (filters && Array.isArray(filters.questNames)) {
        questNames = filters.questNames
            .map(function(name) { return String(name).trim(); })
            .filter(Boolean);
    } else if (filters && filters.questName) {
        // Backward compatibility for legacy payloads.
        questNames = String(filters.questName)
            .split(',')
            .map(function(name) { return name.trim(); })
            .filter(Boolean);
    }

    questFilterState = {
        questNames: questNames,
        trader: (filters && filters.trader) ? String(filters.trader) : ''
    };
    refreshQuestFilterVisibility();
}

function refreshQuestFilterVisibility() {
    let questNames = Array.isArray(questFilterState.questNames)
        ? questFilterState.questNames
            .map(function(name) { return String(name).trim().toLowerCase(); })
            .filter(Boolean)
        : [];
    let questNameSet = new Set(questNames);
    let trader = (questFilterState.trader || '').trim().toLowerCase();
    let hasFilter = questNameSet.size > 0 || (trader.length > 0 && trader !== 'all');

    for (let i = 0; i < cachedMapMarkers.length; i++) {
        let marker = cachedMapMarkers[i];
        if (marker.dataset.markerType !== 'quest') continue;

        marker.classList.remove('quest-filter-hidden');
        if (!hasFilter) continue;

        let markerQuestName = (marker.dataset.questName || '').toLowerCase();
        let nameMatch = questNameSet.size === 0 || questNameSet.has(markerQuestName);
        let traderMatch = !trader || trader === 'all' ||
            (marker.dataset.questTrader || '').toLowerCase() === trader;
        marker.classList.toggle('quest-filter-hidden', !(nameMatch && traderMatch));
    }
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
    setQuestNamesVisibility(!!filters.showQuestNames);
    setHazardVisibility(!!filters.hazards);
    setHazardZoneVisibility(!!filters.hazardZones);
    setSwitchVisibility(!!filters.switches);
    setMarkerTypeVisibility('loot-valuables', !!filters.valuables);
    setMarkerTypeVisibility('loot-battle-pass', !!filters.battlePassDocuments);
    setMarkerTypeVisibility('loot-safe', !!filters.safes);
    setMarkerTypeVisibility('loot-ground-cache', !!filters.groundCaches);
    setMarkerTypeVisibility('loot-lock', !!filters.locks);
    setBtrStopVisibility(!!filters.btrStops);
    setCustomPinsMode(!!filters.customPins);
    refreshRaidExfilHighlights();
    refreshMarkerSearchHighlight();
    refreshQuestFilterVisibility();
}

let raidExfilState = {
    active: false,
    extractMatches: [],
    extractNames: [],
    transitNames: []
};

function normalizeRaidName(name) {
    return (name || '').toLowerCase().replace(/[^a-z0-9]/g, '');
}

function normalizeRaidFaction(faction) {
    return (faction || '').toLowerCase().replace(/[^a-z0-9]/g, '');
}

function buildExtractMatchKey(name, faction) {
    return normalizeRaidName(name) + '|' + normalizeRaidFaction(faction);
}

function setRaidExfilHighlights(state) {
    if (!state) {
        raidExfilState = { active: false, extractMatches: [], extractNames: [], transitNames: [] };
    } else {
        raidExfilState = {
            active: state.active === true,
            extractMatches: state.extractMatches || [],
            extractNames: state.extractNames || [],
            transitNames: state.transitNames || []
        };
    }

    refreshRaidExfilHighlights();
}

function refreshRaidExfilHighlights() {
    let extractMatches = raidExfilState.extractMatches || [];
    let extractIdSet = new Set(extractMatches.map(function(m) { return m.id; }).filter(Boolean));
    let extractKeySet = new Set(extractMatches.map(function(m) {
        return buildExtractMatchKey(m.name, m.faction);
    }));
    let legacyExtractSet = new Set((raidExfilState.extractNames || []).map(normalizeRaidName));
    let transitSet = new Set((raidExfilState.transitNames || []).map(normalizeRaidName));
    let active = raidExfilState.active === true;
    let useLegacyExtractNames = extractMatches.length === 0 && legacyExtractSet.size > 0;

    for (let i = 0; i < cachedMapMarkers.length; i++) {
        let marker = cachedMapMarkers[i];
        marker.classList.remove('raid-available', 'raid-dimmed');

        if (!active)
            continue;

        let markerType = marker.dataset.markerType;
        let markerName = normalizeRaidName(marker.dataset.markerName);

        if (markerType === 'extract') {
            let matched = false;
            let extractId = marker.dataset.extractId;
            let extractKey = buildExtractMatchKey(marker.dataset.markerName, marker.dataset.faction);

            if (extractId && extractIdSet.has(extractId)) {
                matched = true;
            } else if (extractKeySet.has(extractKey)) {
                matched = true;
            } else if (useLegacyExtractNames && legacyExtractSet.has(markerName)) {
                matched = true;
            }

            marker.classList.toggle('raid-available', matched);
            marker.classList.toggle('raid-dimmed', !matched);
        } else if (markerType === 'transit' && transitSet.has(markerName)) {
            marker.classList.add('raid-available');
        } else if (markerType === 'transit') {
            marker.classList.add('raid-dimmed');
        }
    }
}

let mapLevelState = {
    defaultLayerId: null,
    overlayLayerIds: [],
    showBaseLayer: true,
    dimBase: false,
    exclusiveLayers: false,
    activeLevelIds: [],
    levelExtents: []
};

function refreshMarkerLevelVisibility() {
    const extents = mapLevelState.levelExtents || [];
    if (extents.length === 0) {
        for (let i = 0; i < cachedMapMarkers.length; i++) {
            cachedMapMarkers[i].classList.remove('off-level');
        }
        return;
    }

    const activeIds = new Set(mapLevelState.activeLevelIds || []);

    for (let i = 0; i < cachedMapMarkers.length; i++) {
        const marker = cachedMapMarkers[i];
        if (marker.dataset.gameY == null) continue;

        const y = parseFloat(marker.dataset.gameY);
        if (Number.isNaN(y)) {
            marker.classList.remove('off-level');
            continue;
        }

        let onLevel = true;
        let matchedOverlay = false;
        for (const ext of extents) {
            if (y >= ext.minHeight && y < ext.maxHeight) {
                onLevel = activeIds.has(ext.svgLayer);
                matchedOverlay = true;
                break;
            }
        }

        // Default/base floor: hide those labels when an overlay floor is in focus
        // (Labs technical, Icebreaker decks, etc.) so first-floor names don't
        // sit on basement tunnels.
        if (!matchedOverlay) {
            const overlayActive = activeIds.size > 0;
            onLevel = mapLevelState.showBaseLayer !== false &&
                !(mapLevelState.dimBase && overlayActive);
        }

        marker.classList.toggle('off-level', !onLevel);
    }
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
            if (!mapLevelState.showBaseLayer ||
                (mapLevelState.exclusiveLayers && hasActiveOverlay)) {
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
    mapLevelState.exclusiveLayers = state.exclusiveLayers === true;
    mapLevelState.activeLevelIds = state.activeLevelIds || [];
    mapLevelState.levelExtents = state.levelExtents || [];

    refreshMapLevelDisplay();
    refreshMarkerLevelVisibility();
}

function highlightLinkedSwitches(switchIds) {
    let idSet = switchIds && switchIds.length ? new Set(switchIds) : null;

    for (let i = 0; i < cachedMapMarkers.length; i++) {
        let marker = cachedMapMarkers[i];
        if (marker.dataset.markerType !== 'switch') continue;

        marker.classList.toggle(
            'switch-linked',
            !!(idSet && idSet.has(marker.dataset.switchId)));
    }
}

function clearLinkedSwitchHighlights() {
    highlightLinkedSwitches([]);
}

function panMapBy(deltaX, deltaY) {
    panX += deltaX;
    panY += deltaY;
    applyTransform();
    refreshCounterScales();
}

function zoomMapByFactor(zoomFactor, focusX, focusY) {
    let oldScale = scale;
    let newScale = Math.max(0.2, Math.min(oldScale * zoomFactor, 12));
    if (newScale === oldScale) return;

    let mx = focusX != null ? focusX : stage.clientWidth / 2;
    let my = focusY != null ? focusY : stage.clientHeight / 2;
    let ratio = newScale / oldScale;

    panX = mx - (mx - panX) * ratio;
    panY = my - (my - panY) * ratio;

    scale = newScale;
    applyTransform();
    refreshCounterScales();
}

stage.addEventListener('wheel', function(e) {
    e.preventDefault();

    let rect = stage.getBoundingClientRect();
    let mx = e.clientX - rect.left;
    let my = e.clientY - rect.top;
    zoomMapByFactor(e.deltaY < 0 ? 1.15 : 1 / 1.15, mx, my);
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
