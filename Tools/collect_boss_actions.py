#!/usr/bin/env python3
"""
XIVAPI Boss Action Collector v2 for RotationSolver Reborn
Collects all AOE/Stack/Raidwide/Tankbuster action IDs for Savage, Ultimate, and Extreme encounters.
Uses beta.xivapi.com API v2.

v2 FIX: Uses ID clustering to avoid scanning the entire Action sheet.
Common ability names (Akh Morn, Earthquake, etc.) appear across many encounters.
We cluster matching IDs and only scan the tightest cluster per encounter.
"""

import json
import urllib.request
import urllib.parse
import time
import sys
import os
from collections import defaultdict

API_BASE = "https://beta.xivapi.com/api/1"
DELAY = 0.15  # seconds between API calls
CLUSTER_GAP = 150  # IDs within this gap belong to same cluster
SCAN_PADDING = 50   # padding around cluster for range scan
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUTPUT_DIR = os.path.join(SCRIPT_DIR, "..", "Resources")

# ============================================================================
# ENCOUNTER DEFINITIONS
# Each encounter has anchor ability names used to find the ID range,
# then we scan that range for ALL non-player actions.
# ============================================================================

ENCOUNTERS = {
    # ========================================================================
    # ARR COILS (2.x) - Savage equivalent
    # ========================================================================
    "T1-Caduceus": {
        "type": "savage", "expansion": "ARR",
        "anchors": ["Hood Swing", "Regorge", "Steel Scales"]
    },
    "T2-ADS": {
        "type": "savage", "expansion": "ARR",
        "anchors": ["High Voltage", "Repelling Cannons", "Piercing Laser", "Firestream"]
    },
    "T5-Twintania": {
        "type": "savage", "expansion": "ARR",
        "anchors": ["Death Sentence", "Twister", "Liquid Hell", "Aetheric Profusion", "Divebomb"]
    },
    "T6-Rafflesia": {
        "type": "savage", "expansion": "ARR",
        "anchors": ["Blighted Bouquet", "Floral Trap", "Acid Rain", "Devour"]
    },
    "T7-Melusine": {
        "type": "savage", "expansion": "ARR",
        "anchors": ["Cursed Voice", "Cursed Shriek", "Petrifaction", "Circle of Flames", "Venomous Tail"]
    },
    "T8-Avatar": {
        "type": "savage", "expansion": "ARR",
        "anchors": ["Allagan Field", "Ballistic Missile", "Homing Missile", "Gaseous Bomb", "Brainjack"]
    },
    "T9-Nael": {
        "type": "savage", "expansion": "ARR",
        "anchors": ["Lunar Dynamo", "Iron Chariot", "Thermionic Beam", "Ravensbeak", "Heavensfall", "Dalamud Dive", "Bahamut's Claw"]
    },
    "T10-Imdugud": {
        "type": "savage", "expansion": "ARR",
        "anchors": ["Crackle Hiss", "Heat Lightning", "Electrocharge", "Wild Charge", "Critical Rip"]
    },
    "T11-Kaliya": {
        "type": "savage", "expansion": "ARR",
        "anchors": ["Nerve Gas", "Barofield", "Resonance", "Emergency Mode", "Secondary Head"]
    },
    "T12-Phoenix": {
        "type": "savage", "expansion": "ARR",
        "anchors": ["Revelation", "Blackfire", "Flames of Unforgiveness", "Rebirth"]
    },
    "T13-Bahamut": {
        "type": "savage", "expansion": "ARR",
        "anchors": ["Flatten", "Earth Shaker", "Akh Morn", "Gigaflare", "Teraflare", "Tempest Wing"]
    },

    # ========================================================================
    # HW ALEXANDER SAVAGE (3.x)
    # ========================================================================
    "A1S-Oppressor": {
        "type": "savage", "expansion": "HW",
        "anchors": ["Hydrothermal Missile", "Resin Bomb", "Photon Spaser", "Emergency Quarantine", "Hypercompressed Plasma"]
    },
    "A2S-Gobwalker": {
        "type": "savage", "expansion": "HW",
        "anchors": ["Bomb's Away", "Gobstraight", "Gobcut", "Gobswipe"]
    },
    "A3S-LivingLiquid": {
        "type": "savage", "expansion": "HW",
        "anchors": ["Cascade", "Drainage", "Protean Wave", "Sluice", "Hand of Pain", "Splash", "Digititis"]
    },
    "A4S-Manipulator": {
        "type": "savage", "expansion": "HW",
        "anchors": ["Mortal Revolution", "Judgment Nisi", "Perpetual Ray", "Carnage Zero", "Royal Pentacle"]
    },
    "A5S-Faust": {
        "type": "savage", "expansion": "HW",
        "anchors": ["Boost", "Prey", "Shock Therapy", "Gobhook"]
    },
    "A6S-MultiPhase": {
        "type": "savage", "expansion": "HW",
        "anchors": ["Enumeration", "Ultra Flash", "Mega Beam", "Single Buster"]
    },
    "A7S-Quickthinx": {
        "type": "savage", "expansion": "HW",
        "anchors": ["Sizzlespark", "Sizzlebeam", "Uplander Doom", "Zoomdoom"]
    },
    "A8S-BruteJustice": {
        "type": "savage", "expansion": "HW",
        "anchors": ["Apocalyptic Ray", "Final Apocalypse", "Punishing Heat", "Super Jump", "J Storm", "J Wave"]
    },
    "A9S-Refurbisher": {
        "type": "savage", "expansion": "HW",
        "anchors": ["Scrapline", "Stockpile", "Double Scrapline"]
    },
    "A10S-Lamebrix": {
        "type": "savage", "expansion": "HW",
        "anchors": ["Goblin Rush", "Gobrush Rushgob"]
    },
    "A11S-CruiseChaser": {
        "type": "savage", "expansion": "HW",
        "anchors": ["Optical Sight", "Whirlwind", "Spin Crusher", "Laser X Sword", "Photon"]
    },
    "A12S-AlexPrime": {
        "type": "savage", "expansion": "HW",
        "anchors": ["Mega Holy", "Temporal Stasis", "Gravitational Anomaly", "Sacrament", "Inception", "Divine Spear", "Judgment Crystal"]
    },

    # ========================================================================
    # SB OMEGA SAVAGE (4.x)
    # ========================================================================
    "O1S-AlteRoite": {
        "type": "savage", "expansion": "SB",
        "anchors": ["Wyrm Tail", "Twin Bolt", "Charybdis", "Downburst", "Levinbolt", "Roar"]
    },
    "O2S-Catastrophe": {
        "type": "savage", "expansion": "SB",
        "anchors": ["Gravitational Wave", "Earthquake", "Epicenter", "Antilight", "Main Quake", "Demon Eye", "Evilsphere"]
    },
    "O3S-Halicarnassus": {
        "type": "savage", "expansion": "SB",
        "anchors": ["The Queen's Waltz", "Ribbit", "Mindjack", "Place of Power", "The Playing Field"]
    },
    "O4S-Exdeath": {
        "type": "savage", "expansion": "SB",
        "anchors": ["Vacuum Wave", "Black Hole", "Almagest", "Delta Attack", "Grand Cross Alpha", "Grand Cross Delta", "Grand Cross Omega", "Flare"]
    },
    "O5S-PhantomTrain": {
        "type": "savage", "expansion": "SB",
        "anchors": ["Diabolical Whistle", "Doom Strike", "Head On", "Diabolic Light", "Acid Rain"]
    },
    "O6S-Chadarnook": {
        "type": "savage", "expansion": "SB",
        "anchors": ["Flash Fire", "Demonic Stone", "Poltergeist", "Last Kiss", "Divine Lure"]
    },
    "O7S-Guardian": {
        "type": "savage", "expansion": "SB",
        "anchors": ["Atomic Ray", "Arm and Hammer", "Diffractive Plasma", "Magitek Ray", "Missile"]
    },
    "O8S-Kefka": {
        "type": "savage", "expansion": "SB",
        "anchors": ["Hyperdrive", "Graven Image", "Light of Judgment", "Forsaken", "Mana Release", "Thrumming Thunder", "Blizzard Blitz", "Flagrant Fire", "Indolent Will", "Trine"]
    },
    "O9S-Chaos": {
        "type": "savage", "expansion": "SB",
        "anchors": ["Chaotic Dispersion", "Bowels of Agony", "Big Bang", "Blaze", "Cyclone", "Tsunami"]
    },
    "O10S-Midgardsormr": {
        "type": "savage", "expansion": "SB",
        "anchors": ["Protostar", "Thunderstorm", "Cauterize", "Tail End", "Frost Breath", "Akh Rhai"]
    },
    "O11S-OmegaMF": {
        "type": "savage", "expansion": "SB",
        "anchors": ["Ion Efflux", "Pile Pitch", "Mustard Bomb", "Laser Shower", "Flame Thrower"]
    },
    "O12S-Omega": {
        "type": "savage", "expansion": "SB",
        "anchors": ["Cosmo Memory", "Hello, World", "Patch", "Critical Error", "Archive Peripheral", "Index and Archive Peripheral"]
    },

    # ========================================================================
    # ShB EDEN SAVAGE (5.x)
    # ========================================================================
    "E1S-EdenPrime": {
        "type": "savage", "expansion": "ShB",
        "anchors": ["Eden's Gravity", "Fragor Maximus", "Spear of Paradise", "Vice and Virtue", "Dimensional Shift", "Eden's Flare", "Delta Attack"]
    },
    "E2S-Voidwalker": {
        "type": "savage", "expansion": "ShB",
        "anchors": ["Shadowflame", "Entropy", "Hell Wind", "Shadoweye", "Dark Fire III", "Unholy Darkness", "Punishing Ray"]
    },
    "E3S-Leviathan": {
        "type": "savage", "expansion": "ShB",
        "anchors": ["Tidal Wave", "Maelstrom", "Temporary Current", "Undersea Quake", "Black Smokers", "Drenching Pulse", "Tidal Rage"]
    },
    "E4S-Titan": {
        "type": "savage", "expansion": "ShB",
        "anchors": ["Stonecrusher", "Weight of the Land", "Evil Earth", "Voice of the Land", "Earthen Fury", "Geocrush", "Tumult", "Magnitude 5.0", "Crumbling Down"]
    },
    "E5S-Ramuh": {
        "type": "savage", "expansion": "ShB",
        "anchors": ["Tribunal Summons", "Judgment Volts", "Crippling Blow", "Fury's Fourteen", "Chain Lightning", "Stepped Leader", "Volt Strike"]
    },
    "E6S-GarudaIfrit": {
        "type": "savage", "expansion": "ShB",
        "anchors": ["Hands of Hell", "Hands of Flame", "Instant Incineration", "Air Bump", "Vacuum Slice", "Conflag Strike", "Strike Spark"]
    },
    "E7S-IdolOfDarkness": {
        "type": "savage", "expansion": "ShB",
        "anchors": ["Empty Wave", "Unjoined Aspect", "Words of Night", "Strength in Numbers", "Away with Thee"]
    },
    "E8S-Shiva": {
        "type": "savage", "expansion": "ShB",
        "anchors": ["Diamond Frost", "Heavenly Strike", "Biting Frost", "Driving Frost", "Double Slap", "Light Rampant", "Morn Afah", "Absolute Zero", "Mirror, Mirror"]
    },
    "E9S-CloudOfDarkness": {
        "type": "savage", "expansion": "ShB",
        "anchors": ["Ground-razing Particle Beam", "Wide-angle Particle Beam", "Zero-form Particle Beam", "Obscure Light", "Flood of Obscurity", "Empty Plane"]
    },
    "E10S-Shadowkeeper": {
        "type": "savage", "expansion": "ShB",
        "anchors": ["Deepshadow Nova", "Throne of Shadow", "Shadow's Edge", "Umbra Smash", "Giga Slash", "Pitch Bog", "Void Gate"]
    },
    "E11S-Fatebreaker": {
        "type": "savage", "expansion": "ShB",
        "anchors": ["Burnt Strike", "Bound of Faith", "Turn of the Heavens", "Prismatic Deception", "Elemental Break", "Burnished Glory"]
    },
    "E12S-EdenPromise": {
        "type": "savage", "expansion": "ShB",
        "anchors": ["Maleficium", "Formless Judgment", "Shockwave Pulsar", "Spirit Taker", "Darkest Dance", "Diamond Dust", "Stock", "Release"]
    },

    # ========================================================================
    # EW PANDAEMONIUM SAVAGE (6.x)
    # ========================================================================
    "P1S-Erichthonios": {
        "type": "savage", "expansion": "EW",
        "anchors": ["Warder's Wrath", "Aetherchain", "Intemperance", "Heavy Hand", "Pitiless Flail", "Gaoler's Flail", "True Holy", "Slam Shut", "Shining Cells"]
    },
    "P2S-Hippokampos": {
        "type": "savage", "expansion": "EW",
        "anchors": ["Sewage Deluge", "Murky Depths", "Coherence", "Predatory Avarice", "Channeling Overflow", "Tainted Flood", "Shockwave", "Dissociation", "Spoken Cataract", "Winged Cataract"]
    },
    "P3S-Phoinix": {
        "type": "savage", "expansion": "EW",
        "anchors": ["Experimental Fireplume", "Heat of Condemnation", "Darkened Fire", "Flames of Asphodelos", "Dead Rebirth", "Life's Agonies", "Fledgling Flight", "Sun's Pinion", "Trail of Condemnation"]
    },
    "P4S-Hesperos": {
        "type": "savage", "expansion": "EW",
        "anchors": ["Decollation", "Elegant Evisceration", "Pinax", "Bloodrake", "Searing Stream", "Demigod Double", "Ultimate Impulse", "Periaktoi", "Setting the Scene", "Director's Belone"]
    },
    "P5S-ProtoCarbuncle": {
        "type": "savage", "expansion": "EW",
        "anchors": ["Ruby Glow", "Topaz Stones", "Topaz Cluster", "Venom Pool", "Sonic Howl", "Raging Claw", "Searing Ray", "Sonic Shatter"]
    },
    "P6S-Hegemone": {
        "type": "savage", "expansion": "EW",
        "anchors": ["Hemitheos's Dark IV", "Choros Ixou", "Synergy", "Dark Dome", "Cachexia", "Aetheric Polyominoid", "Transmission"]
    },
    "P7S-Agdistis": {
        "type": "savage", "expansion": "EW",
        "anchors": ["Bough of Attis", "Spark of Life", "Forbidden Fruit", "Immortal's Obol", "Inviolate Bonds", "Dispersed Aero II", "Condensed Aero II"]
    },
    "P8S-Hephaistos": {
        "type": "savage", "expansion": "EW",
        "anchors": ["Genesis of Flame", "Volcanic Torches", "Ektothermos", "Flameviper", "Sunforge", "Octaflare", "Tetraflare", "Diflare", "Natural Alignment", "High Concept"]
    },
    "P9S-Kokytos": {
        "type": "savage", "expansion": "EW",
        "anchors": ["Gluttony's Augur", "Ravening", "Dualspell", "Ascendant Fist", "Archaic Demolish", "Chimeric Succession", "Levinstrike Summoning", "Scrambled Succession"]
    },
    "P10S-Pandaemonium": {
        "type": "savage", "expansion": "EW",
        "anchors": ["Pandaemoniac Meltdown", "Soul Grasp", "Wicked Step", "Pandaemoniac Pillars", "Dividing Wings", "Touchdown", "Harrowing Hell", "Silkspit"]
    },
    "P11S-Themis": {
        "type": "savage", "expansion": "EW",
        "anchors": ["Eunomia", "Jury Overruling", "Upheld Overruling", "Divisive Overruling", "Letter of the Law", "Arcane Revelation", "Shadowed Messengers", "Lightstream", "Dark Current", "Blinding Light", "Twofold Revelation"]
    },
    "P12S-Athena": {
        "type": "savage", "expansion": "EW",
        "anchors": ["On the Soul", "Glaukopis", "Trinity of Souls", "Superchain Theory", "Palladian Grasp", "Crush Helm", "Caloric Theory", "Ekpyrosis", "Pangenesis", "Ultima", "Gaiaochos"]
    },

    # ========================================================================
    # DT AAC SAVAGE (7.x)
    # ========================================================================
    "M1S-BlackCat": {
        "type": "savage", "expansion": "DT",
        "anchors": ["Mouser", "Quadruple Crossing", "Biscuit Maker", "Bloody Scratch", "Shockwave", "Predaceous Pounce", "Copycat", "Leaping Quadruple Crossing", "One-two Paw"]
    },
    "M2S-HoneyBLovely": {
        "type": "savage", "expansion": "DT",
        "anchors": ["Call Me Honey", "Bee Sting", "Drop of Venom", "Honey Beeline", "Stinging Slash", "Blinding Love", "Tempting Twist", "Poison Sting"]
    },
    "M3S-BruteBomber": {
        "type": "savage", "expansion": "DT",
        "anchors": ["Octuple Lariat", "Brutish Swing", "Knuckle Sandwich", "Doping Draught", "Barbarous Barrage", "Tag Team", "Lariat Combo", "Murderous Mist", "Final Fuse"]
    },
    "M4S-WickedThunder": {
        "type": "savage", "expansion": "DT",
        "anchors": ["Wicked Thunder", "Wrath of Zeus", "Electrope Edge", "Bewitching Flight", "Wicked Bolt", "Witch Hunt", "Stampeding Thunder", "Soulshock", "Midnight Sabbath", "Cross Tail Switch", "Sunrise Sabbath", "Twilight Sabbath", "Ion Cluster", "Electron Stream"]
    },
    "M5S": {
        "type": "savage", "expansion": "DT",
        "anchors": ["Frosting Fracas", "Chill Cauldron"]
    },
    "M6S": {
        "type": "savage", "expansion": "DT",
        "anchors": ["Mousse Mural", "Pâtissière's Art"]
    },
    "M7S": {
        "type": "savage", "expansion": "DT",
        "anchors": ["Chilling Chirp", "Peck and Poison"]
    },
    "M8S": {
        "type": "savage", "expansion": "DT",
        "anchors": ["Extraplanar Pursuit", "Howling Blade"]
    },
    "M9S": {
        "type": "savage", "expansion": "DT",
        "anchors": ["Crown of Arcadia", "Charybdistopia"]
    },
    "M10S": {
        "type": "savage", "expansion": "DT",
        "anchors": ["Overrun", "Trample"]
    },
    "M11S": {
        "type": "savage", "expansion": "DT",
        "anchors": ["Dance of Domination", "Crown of Arcadia", "Charybdistopia", "Meteorain", "Majestic Meteor", "Ecliptic Stampede", "Flatliner", "Raw Steel Trophy", "Explosion"]
    },
    "M12S": {
        "type": "savage", "expansion": "DT",
        "anchors": ["The Fixer", "Splattershed", "Refreshing Overkill", "Unmitigated Explosion", "Idyllic Dream", "Arcadian Hell", "Arcadian Hell II", "Arcadian Hell III"]
    },

    # ========================================================================
    # ULTIMATES
    # ========================================================================
    "UCoB": {
        "type": "ultimate", "expansion": "SB",
        "anchors": ["Twister", "Fireball", "Liquid Hell", "Plummet", "Generate",
                     "Lunar Dynamo", "Iron Chariot", "Thermionic Beam", "Meteor Stream", "Heavensfall", "Dalamud Dive",
                     "Flatten", "Earth Shaker", "Gigaflare", "Exaflare", "Tempest Wing",
                     "Morn Afah", "Aetheric Profusion", "Megaflare"]
    },
    "UWU": {
        "type": "ultimate", "expansion": "SB",
        "anchors": ["Slipstream", "Mistral Song", "Aerial Blast", "Feather Rain", "Wicked Wheel", "Mesohigh", "Friction", "Downburst",
                     "Crimson Cyclone", "Eruption", "Hellfire", "Vulcan Burst", "Flaming Crush", "Incinerate", "Infernal Howl",
                     "Mountain Buster", "Weight of the Land", "Geocrush", "Rock Buster", "Landslide", "Earthen Fury", "Tumult",
                     "Tank Purge", "Homing Lasers", "Viscous Aetheroplasm", "Diffractive Laser", "Aetheric Boom", "Eye of the Storm",
                     "Radiant Plume", "Ceruleum Vent", "Citadel Buster"]
    },
    "TEA": {
        "type": "ultimate", "expansion": "ShB",
        "anchors": ["Fluid Strike", "Protean Wave", "Cascade", "Splash", "Sluice", "Drainage", "Hand of Pain",
                     "Chakram", "Spin Crusher", "Whirlwind", "Photon", "Super Jump",
                     "Temporal Stasis", "Sacrament", "Mega Holy", "Inception Formation", "Chastening Heat",
                     "Almighty Judgment", "Irresistible Grace", "Ordained Punishment", "Fate Calibration"]
    },
    "DSR": {
        "type": "ultimate", "expansion": "EW",
        "anchors": ["Holiest of Holy", "Ascalon's Mercy", "Brightblade's Steel", "Heavens' Stake", "Pure of Heart",
                     "Ultimate End", "Broad Swing", "Aetheric Burst", "Heavenly Heel", "Gnash and Lash", "Lash and Gnash",
                     "Final Chorus", "Darkdragon Dive", "Soul Tether", "Mortal Vow", "Eye of the Tyrant",
                     "Hallowed Wings", "Akh Morn's Edge", "Wyrmsbreath",
                     "Alternative End", "Exaflare's Edge"]
    },
    "TOP": {
        "type": "ultimate", "expansion": "EW",
        "anchors": ["Program Loop", "Pantokrator", "Condensed Wave Cannon", "Wave Cannon", "Solar Ray", "Storage Violation",
                     "Beyond Defense", "Cosmo Dive", "Pile Pitch", "Laser Shower", "Synthetic Shield",
                     "Hello, World", "Critical Error", "Oversampled Wave Cannon",
                     "Blue Screen", "Ion Efflux",
                     "Cosmo Memory", "Magic Number", "Blind Faith", "Run: ****mi* (Delta Version)", "Run: ****mi* (Sigma Version)", "Run: ****mi* (Omega Version)", "Cosmo Arrow"]
    },
    "FRU": {
        "type": "ultimate", "expansion": "DT",
        "anchors": ["Cyclonic Break", "Quadruple Slap", "Burnished Glory", "Fall of Faith", "Utopian Sky",
                     "Diamond Dust", "Axe Kick", "Scythe Kick", "Hallowed Ray", "Banish III", "Light Rampant", "The House of Light",
                     "Ultimate Relativity", "Shell Crusher", "Black Halo", "Shockwave Pulsar", "Somber Dance",
                     "Wings Dark and Light", "Polarizing Strikes", "Paradise Regained", "Crystallize Time", "Materialization",
                     "Memory's End", "Fulgent Blade"]
    },

    # ========================================================================
    # ARR EXTREMES (2.x)
    # ========================================================================
    "Ifrit-EX": {
        "type": "extreme", "expansion": "ARR",
        "anchors": ["Hellfire", "Eruption", "Crimson Cyclone", "Radiant Plume", "Incinerate", "Vulcan Burst"]
    },
    "Garuda-EX": {
        "type": "extreme", "expansion": "ARR",
        "anchors": ["Aerial Blast", "Mistral Song", "Slipstream", "Wicked Wheel", "Downburst", "Friction", "Feather Rain"]
    },
    "Titan-EX": {
        "type": "extreme", "expansion": "ARR",
        "anchors": ["Earthen Fury", "Weight of the Land", "Geocrush", "Rock Buster", "Landslide", "Mountain Buster", "Tumult"]
    },
    "Leviathan-EX": {
        "type": "extreme", "expansion": "ARR",
        "anchors": ["Tidal Wave", "Tidal Roar", "Aqua Burst", "Spinning Dive", "Waterspout", "Body Slam"]
    },
    "Ramuh-EX": {
        "type": "extreme", "expansion": "ARR",
        "anchors": ["Judgment Bolt", "Thunderstorm", "Thunderspark", "Shock Strike", "Rolling Thunder", "Chaotic Strike"]
    },
    "Shiva-EX": {
        "type": "extreme", "expansion": "ARR",
        "anchors": ["Diamond Dust", "Heavenly Strike", "Hailstorm", "Absolute Zero", "Avalanche", "Permafrost", "Glacier Bash", "Whiteout", "Dreams of Ice"]
    },
    "Moggle-Mog-EX": {
        "type": "extreme", "expansion": "ARR",
        "anchors": ["Memento Moogle", "Pom Holy", "Pom Flare"]
    },
    "Ultima-EX": {
        "type": "extreme", "expansion": "ARR",
        "anchors": ["Aetheric Boom", "Ceruleum Vent", "Citadel Buster", "Radiant Plume", "Eye of the Storm", "Homing Lasers", "Tank Purge", "Diffractive Laser"]
    },

    # ========================================================================
    # HW EXTREMES (3.x)
    # ========================================================================
    "Bismarck-EX": {
        "type": "extreme", "expansion": "HW",
        "anchors": ["Breach Blast", "Sharp Gust"]
    },
    "Ravana-EX": {
        "type": "extreme", "expansion": "HW",
        "anchors": ["Bloody Fuller", "Liberation", "Atma-Linga", "Blinding Blade", "The Seeing Left", "The Seeing Right", "Swift Liberation", "Final Liberation"]
    },
    "Thordan-EX": {
        "type": "extreme", "expansion": "HW",
        "anchors": ["Ascalon's Might", "Lightning Storm", "Ancient Quaga", "Heavenly Heel", "The Dragon's Eye", "Holy Shield Bash", "Sacred Cross", "Ultimate End", "Knights of the Round"]
    },
    "Sephirot-EX": {
        "type": "extreme", "expansion": "HW",
        "anchors": ["Ein Sof", "Pillar of Mercy", "Force Field", "Fiendish Rage", "Chesed", "Gevurah", "Da'at"]
    },
    "Nidhogg-EX": {
        "type": "extreme", "expansion": "HW",
        "anchors": ["Akh Morn", "Cauterize", "Hot Wing", "Hot Tail", "Scarlet Price", "Deafening Bellow", "Mortal Chorus", "Massacre"]
    },
    "Sophia-EX": {
        "type": "extreme", "expansion": "HW",
        "anchors": ["Thunder II", "Thunder III", "Aero III", "Cintamani", "Gnosis", "Quasar", "Light Dew", "Execute"]
    },
    "Zurvan-EX": {
        "type": "extreme", "expansion": "HW",
        "anchors": ["Flare Star", "Southern Cross", "Ciclicle", "Tail End", "Biting Halberd", "Ahura Mazda", "Infinite Fire", "Infinite Ice"]
    },

    # ========================================================================
    # SB EXTREMES (4.x)
    # ========================================================================
    "Susano-EX": {
        "type": "extreme", "expansion": "SB",
        "anchors": ["Assail", "Rasen Kaikyo", "Ukehi", "Stormsplitter", "Brightstorm", "Ama-no-Iwato"]
    },
    "Lakshmi-EX": {
        "type": "extreme", "expansion": "SB",
        "anchors": ["Pull of Light", "Stotram", "The Pall of Light", "Path of Light", "Alluring Arm", "Divine Denial", "Divine Desire", "Divine Doubt"]
    },
    "Shinryu-EX": {
        "type": "extreme", "expansion": "SB",
        "anchors": ["Tidal Wave", "Aerial Blast", "Diamond Dust", "Hellfire", "Judgment Bolt", "Earthen Fury", "Protostar", "Akh Morn", "Akh Rhai", "Benevolence", "Dark Matter", "Tail Slap"]
    },
    "Byakko-EX": {
        "type": "extreme", "expansion": "SB",
        "anchors": ["Storm Pulse", "Heavenly Strike", "State of Shock", "Sweep the Leg", "Highest Stakes", "Hundredfold Havoc", "Unrelenting Anguish", "Bombogenesis"]
    },
    "Tsukuyomi-EX": {
        "type": "extreme", "expansion": "SB",
        "anchors": ["Reprimand", "Nightfall", "Nightbloom", "Tsukiyomi", "Lunacy", "Torment Unto Death", "Supreme Selenomancy", "Dark Blade", "Bright Blade"]
    },
    "Suzaku-EX": {
        "type": "extreme", "expansion": "SB",
        "anchors": ["Screams of the Damned", "Phantom Flurry", "Ruthless Refrain", "Southron Star", "Scarlet Fever", "Mesmerizing Melody", "Rout of the Inferno"]
    },
    "Seiryu-EX": {
        "type": "extreme", "expansion": "SB",
        "anchors": ["Serpent Ascending", "Serpent Descending", "Summon Shiki", "Forbidden Arts", "Kanabo", "Strength of Spirit", "Handprint", "Onmyo Sigil", "Coursing River", "Great Typhoon"]
    },

    # ========================================================================
    # ShB EXTREMES (5.x)
    # ========================================================================
    "Titania-EX": {
        "type": "extreme", "expansion": "ShB",
        "anchors": ["Bright Sabbath", "Phantom Rune", "Mist Rune", "Flame Rune", "Growth Rune", "Divination Rune", "Being Mortal", "Midsummer Night's Dream"]
    },
    "Innocence-EX": {
        "type": "extreme", "expansion": "ShB",
        "anchors": ["Shadowreaver", "Daybreak", "Righteous Bolt", "Holy Sword", "Light Pillar", "Scold's Bridle", "Starbirth", "Beatific Vision", "Winged Reprobation", "God Ray"]
    },
    "Hades-EX": {
        "type": "extreme", "expansion": "ShB",
        "anchors": ["Ravenous Assault", "Shadow Spread", "Bad Faith", "Broken Faith", "Dark Seal", "Gigantomachy", "Polydegmon's Purgation", "Captivity", "Dual Strike", "Titanomachy"]
    },
    "RubyWeapon-EX": {
        "type": "extreme", "expansion": "ShB",
        "anchors": ["Optimized Ultima", "Stamp", "Ravensclaw", "Ruby Ray", "Homing Lasers", "High-powered Homing Lasers", "Ruby Sphere", "Ruby Dynamics", "Negative Personae", "Negative Aura"]
    },
    "Varis-EX": {
        "type": "extreme", "expansion": "ShB",
        "anchors": ["Altius", "Terminus Est", "Citius", "Loaded Gunhilt", "Festina Lente", "Alea Iacta Est"]
    },
    "EmeraldWeapon-EX": {
        "type": "extreme", "expansion": "ShB",
        "anchors": ["Emerald Beam", "Optimized Ultima", "Divide Et Impera", "Primus Terminus Est", "Tertius Terminus Est", "Sidescathe", "Legio Phantasmatis"]
    },
    "DiamondWeapon-EX": {
        "type": "extreme", "expansion": "ShB",
        "anchors": ["Diamond Rain", "Adamant Purge", "Photon Burst", "Auri Arts", "Vertical Cleave", "Flood Ray"]
    },

    # ========================================================================
    # EW EXTREMES (6.x)
    # ========================================================================
    "Zodiark-EX": {
        "type": "extreme", "expansion": "EW",
        "anchors": ["Kokytos", "Styx", "Ania", "Exoterikos", "Paradeigma", "Astral Flow", "Phlegethon", "Algedon", "Adikia", "Triple Esoteric Ray", "Astral Eclipse"]
    },
    "Hydaelyn-EX": {
        "type": "extreme", "expansion": "EW",
        "anchors": ["Heros's Radiance", "Magos's Radiance", "Mousa's Scorn", "Highest Holy", "Crystallize", "Equinox", "Heros's Sundering", "Radiant Halo", "Aureole", "Lateral Aureole", "Heros's Glory", "Parhelic Circle"]
    },
    "Endsinger-EX": {
        "type": "extreme", "expansion": "EW",
        "anchors": ["Elegeia Unforgotten", "Elegeia", "Grip of Despair", "Planetes", "Hubris", "Elenchos", "Telos", "Galaxias", "Katasterismoi", "Telomania"]
    },
    "Barbariccia-EX": {
        "type": "extreme", "expansion": "EW",
        "anchors": ["Void Aero IV", "Savage Barbery", "Hair Raid", "Hair Flay", "Teasing Tangles", "Secret Breeze", "Tornado Chain", "Curling Iron", "Knuckle Drum", "Blow Away", "Brittle Boulder", "Boulder Break", "Trample", "Winding Gale", "Playful Breeze"]
    },
    "Rubicante-EX": {
        "type": "extreme", "expansion": "EW",
        "anchors": ["Inferno", "Ordeal of Purgation", "Arch Inferno", "Shattering Heat", "Scalding Signal", "Blazing Rapture", "Dualfire", "Flamespire Brand", "Flamespire Claw", "Ghastly Torch", "Ghastly Wind", "Ghastly Flame", "Total Immolation", "Sweeping Immolation"]
    },
    "Golbez-EX": {
        "type": "extreme", "expansion": "EW",
        "anchors": ["Terrastorm", "Lingering Spark", "Binding Cold", "Black Fang", "Void Meteor", "Arctic Assault", "Double Meteor", "Azdaja's Shadow", "Gale Sphere", "Void Stardust", "Eventide Fall", "Eventide Triad", "Immolate", "Rising Beacon", "Burning Shade"]
    },
    "Zeromus-EX": {
        "type": "extreme", "expansion": "EW",
        "anchors": ["Abyssal Nox", "Sable Thread", "Visceral Whirl", "Dark Matter", "Big Bang", "Big Crunch", "Meteor Impact", "Dimensional Surge", "Flare", "Nox", "Branding Flare", "Sparking Flare", "Rend the Rift", "Nostalgia", "Flow of the Abyss"]
    },

    # ========================================================================
    # DT EXTREMES (7.x)
    # ========================================================================
    "Valigarmanda-EX": {
        "type": "extreme", "expansion": "DT",
        "anchors": ["Susurrant Breath", "Strangling Coil", "Slithering Strike", "Skyruin", "Calamitous Cry", "Hail of Feathers", "Arcane Lightning", "Ice Talon", "Freezing Dust", "Northern Cross", "Chilling Cataclysm", "Ruinfall", "Thunderous Breath", "Disaster Zone"]
    },
    "ZoraalJa-EX": {
        "type": "extreme", "expansion": "DT",
        "anchors": ["Dawn of an Age", "Multiscript", "Siege of Vollok", "Might of Vollok", "Bitter Whirlwind", "Actualize", "Half Full", "Greater Gateway", "Forward Edge", "Backward Edge", "Regicidal Rage", "Dual Blow", "Half Circuit"]
    },
    "Everkeep-EX": {
        "type": "extreme", "expansion": "DT",
        "anchors": ["Coronation", "Authority Eternal", "Legitimate Force", "Virtual Shift", "Prosecution of War", "Absolute Authority", "Powerful Light", "Powerful Gust"]
    },
    "Sphene-EX": {
        "type": "extreme", "expansion": "DT",
        "anchors": ["Coronation", "Royal Domain", "Aethertithe", "Dimensional Distortion", "Abyssal Embrace"]
    },
}


# ============================================================================
# API FUNCTIONS
# ============================================================================

request_count = 0

def fetch_json(url, retries=3):
    global request_count
    for attempt in range(retries):
        try:
            req = urllib.request.Request(url, headers={"User-Agent": "RSR-DataCollector/1.0"})
            with urllib.request.urlopen(req, timeout=15) as resp:
                request_count += 1
                return json.loads(resp.read().decode())
        except Exception as e:
            if attempt < retries - 1:
                time.sleep(1.0)
            else:
                return None
    return None


def search_action(name):
    """Search for an action by exact name match."""
    time.sleep(DELAY)
    encoded_query = urllib.parse.quote(f'Name="{name}"')
    url = f"{API_BASE}/search?sheets=Action&query={encoded_query}&limit=100"
    data = fetch_json(url)
    results = []
    if data and "results" in data:
        for r in data["results"]:
            if r.get("score", 0) >= 1.0:
                results.append(r["row_id"])
    return results


def get_actions_batch(start_id, count=200):
    """Get a batch of actions starting from start_id."""
    time.sleep(DELAY)
    url = (f"{API_BASE}/sheet/Action"
           f"?fields=Name,IsPlayerAction,CastType,EffectRange,Range,Cast100ms"
           f"&limit={count}&after={start_id - 1}")
    data = fetch_json(url)
    results = []
    if data and "rows" in data:
        for row in data["rows"]:
            f = row.get("fields", {})
            results.append({
                "id": row["row_id"],
                "name": f.get("Name", ""),
                "is_player": f.get("IsPlayerAction", True),
                "cast_type": f.get("CastType", 0),
                "effect_range": f.get("EffectRange", 0),
                "range": f.get("Range", 0),
                "cast_time_s": round((f.get("Cast100ms", 0) or 0) / 10.0, 1),
            })
    return results


def get_single_action(action_id):
    """Get details for a single action."""
    time.sleep(DELAY)
    url = (f"{API_BASE}/sheet/Action/{action_id}"
           f"?fields=Name,IsPlayerAction,CastType,EffectRange,Range,Cast100ms")
    data = fetch_json(url)
    if data and "fields" in data:
        f = data["fields"]
        return {
            "id": action_id,
            "name": f.get("Name", ""),
            "is_player": f.get("IsPlayerAction", True),
            "cast_type": f.get("CastType", 0),
            "effect_range": f.get("EffectRange", 0),
            "range": f.get("Range", 0),
            "cast_time_s": round((f.get("Cast100ms", 0) or 0) / 10.0, 1),
        }
    return None


def scan_range(start_id, end_id):
    """Scan a range of IDs and return all non-player actions with names."""
    results = []
    cursor = start_id
    while cursor <= end_id:
        batch = get_actions_batch(cursor, 500)
        if not batch:
            break
        for a in batch:
            if a["id"] > end_id:
                return results
            if not a["is_player"] and a["name"]:
                results.append(a)
        if len(batch) < 500:
            break
        cursor = batch[-1]["id"] + 1
    return results


# ============================================================================
# CLASSIFICATION
# ============================================================================

def classify_action(action):
    """Classify action based on CastType and EffectRange."""
    ct = action.get("cast_type", 0)
    er = action.get("effect_range", 0)

    # CastType meanings (approximate):
    # 1 = single target
    # 2 = circle AoE (on target)
    # 3 = cone
    # 4 = line
    # 5 = circle AoE (ground target / on caster)
    # 7+ = various special types (donut, cross, etc.)

    if ct == 1:
        return "single_target"  # likely tankbuster
    elif ct in (2, 5, 8, 10, 11, 12, 13):
        if er >= 30:
            return "raidwide"
        elif er >= 6:
            return "aoe"
        else:
            return "small_aoe"
    elif ct in (3,):
        return "cone"
    elif ct in (4,):
        return "line"
    else:
        if er >= 30:
            return "raidwide"
        return "other"


# ============================================================================
# CLUSTERING LOGIC (v2 FIX)
# ============================================================================

def find_clusters(ids, gap=CLUSTER_GAP):
    """Group IDs into clusters where consecutive IDs are within 'gap' of each other."""
    if not ids:
        return []
    sorted_ids = sorted(ids)
    clusters = [[sorted_ids[0]]]
    for i in range(1, len(sorted_ids)):
        if sorted_ids[i] - sorted_ids[i - 1] <= gap:
            clusters[-1].append(sorted_ids[i])
        else:
            clusters.append([sorted_ids[i]])
    return clusters


def best_cluster(ids, gap=CLUSTER_GAP):
    """Return the largest cluster of IDs (most anchor matches = most likely the right encounter)."""
    clusters = find_clusters(ids, gap)
    if not clusters:
        return []
    return max(clusters, key=len)


# ============================================================================
# MAIN
# ============================================================================

def main():
    print("=" * 70)
    print("XIVAPI Boss Action Collector v2 (with clustering)")
    print("=" * 70)
    print()

    all_encounter_data = {}
    all_area_ids = set()
    all_tank_ids = set()

    total_encounters = len(ENCOUNTERS)

    for idx, (enc_key, enc_data) in enumerate(ENCOUNTERS.items(), 1):
        enc_type = enc_data["type"]
        expansion = enc_data["expansion"]
        anchors = enc_data["anchors"]

        print(f"[{idx}/{total_encounters}] {enc_key} ({enc_type}/{expansion}) - {len(anchors)} abilities...")

        all_discovered_ids = []
        name_to_ids = {}

        # Phase 1: Search for anchor abilities
        for anchor_name in anchors:
            ids = search_action(anchor_name)
            if ids:
                name_to_ids[anchor_name] = ids
                all_discovered_ids.extend(ids)
                sys.stdout.write(f"  {anchor_name}: {len(ids)} hits ")
                sys.stdout.write(f"[{', '.join(str(i) for i in sorted(ids)[:5])}{'...' if len(ids)>5 else ''}]\n")
            else:
                sys.stdout.write(f"  {anchor_name}: -\n")
            sys.stdout.flush()

        if not all_discovered_ids:
            print(f"  SKIP: No IDs found")
            continue

        # Phase 2: CLUSTER IDs - find the tightest group
        cluster = best_cluster(all_discovered_ids)
        cluster_set = set(cluster)

        if len(cluster) < 2:
            # Fallback: if only 1 ID in best cluster, use it with small padding
            scan_start = max(1, cluster[0] - SCAN_PADDING)
            scan_end = cluster[0] + SCAN_PADDING
        else:
            scan_start = max(1, min(cluster) - SCAN_PADDING)
            scan_end = max(cluster) + SCAN_PADDING

        total_ids = len(all_discovered_ids)
        cluster_ids = len(cluster)
        dropped = total_ids - cluster_ids
        print(f"  Cluster: {cluster_ids}/{total_ids} IDs (dropped {dropped} outliers), range {scan_start}-{scan_end} ({scan_end-scan_start} wide)")

        # Phase 3: Scan the cluster range
        nearby_actions = scan_range(scan_start, scan_end)
        print(f"  Found {len(nearby_actions)} non-player actions in range")

        # Phase 4: Classify and store
        encounter_actions = {
            "encounter": enc_key,
            "type": enc_type,
            "expansion": expansion,
            "cluster_range": [scan_start, scan_end],
            "anchor_ids": sorted(cluster_set),
            "actions": {}
        }

        for action in nearby_actions:
            cls = classify_action(action)
            if cls not in encounter_actions["actions"]:
                encounter_actions["actions"][cls] = []
            encounter_actions["actions"][cls].append({
                "id": action["id"],
                "name": action["name"],
                "cast_type": action["cast_type"],
                "effect_range": action["effect_range"],
                "cast_time_s": action["cast_time_s"],
            })

            # Add to flat lists
            if cls in ("raidwide", "aoe", "cone", "line"):
                all_area_ids.add(action["id"])
            elif cls == "single_target":
                all_tank_ids.add(action["id"])

        all_encounter_data[enc_key] = encounter_actions

        # Count summary
        total_actions = sum(len(v) for v in encounter_actions["actions"].values())
        parts = [f"{cls}: {len(acts)}" for cls, acts in sorted(encounter_actions["actions"].items())]
        print(f"  => {total_actions} actions ({', '.join(parts)})")

    # ========================================================================
    # OUTPUT
    # ========================================================================
    print()
    print("=" * 70)
    print("RESULTS SUMMARY")
    print("=" * 70)
    print(f"Total encounters processed: {len(all_encounter_data)}")
    print(f"Total area/AOE IDs found: {len(all_area_ids)}")
    print(f"Total tank/single-target IDs found: {len(all_tank_ids)}")
    print(f"Total API requests: {request_count}")

    # Save structured encounter data
    output_file = os.path.join(OUTPUT_DIR, "EncounterActions.json")
    with open(output_file, "w", encoding="utf-8") as f:
        json.dump(all_encounter_data, f, indent=2, ensure_ascii=False)
    print(f"\nStructured data saved to: {output_file}")

    # Load existing area IDs and merge
    area_file = os.path.join(OUTPUT_DIR, "HostileCastingArea.json")
    try:
        with open(area_file, "r") as f:
            existing_area = set(json.load(f))
    except:
        existing_area = set()

    new_area = all_area_ids - existing_area
    merged_area = sorted(existing_area | all_area_ids)
    with open(area_file, "w", encoding="utf-8") as f:
        json.dump(merged_area, f, indent=2)
    print(f"HostileCastingArea.json: {len(existing_area)} existing + {len(new_area)} new = {len(merged_area)} total")

    # Load existing tank IDs and merge
    tank_file = os.path.join(OUTPUT_DIR, "HostileCastingTank.json")
    try:
        with open(tank_file, "r") as f:
            existing_tank = set(json.load(f))
    except:
        existing_tank = set()

    new_tank = all_tank_ids - existing_tank
    merged_tank = sorted(existing_tank | all_tank_ids)
    with open(tank_file, "w", encoding="utf-8") as f:
        json.dump(merged_tank, f, indent=2)
    print(f"HostileCastingTank.json: {len(existing_tank)} existing + {len(new_tank)} new = {len(merged_tank)} total")

    # Save new IDs list for easy review
    new_ids_file = os.path.join(OUTPUT_DIR, "NewIDs.json")
    with open(new_ids_file, "w", encoding="utf-8") as f:
        json.dump({
            "new_area_ids": sorted(new_area),
            "new_tank_ids": sorted(new_tank),
        }, f, indent=2)
    print(f"New IDs saved to: {new_ids_file}")

    print(f"\nDone! Total API requests: {request_count}")


if __name__ == "__main__":
    main()
