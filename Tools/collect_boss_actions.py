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

    # ========================================================================
    # ARR DUNGEONS (2.x)
    # ========================================================================
    "Sastasha": {
        "type": "dungeon", "expansion": "ARR",
        "anchors": ["Slime Bubble", "Tail Screw"]
    },
    "Tam-Tara-Deepcroft": {
        "type": "dungeon", "expansion": "ARR",
        "anchors": ["Condemnation", "Void Fire II"]
    },
    "Copperbell-Mines": {
        "type": "dungeon", "expansion": "ARR",
        "anchors": ["Grand Slam", "Colossal Slam"]
    },
    "Halatali": {
        "type": "dungeon", "expansion": "ARR",
        "anchors": ["Fire II", "Double Sever", "Firewall"]
    },
    "Thousand-Maws": {
        "type": "dungeon", "expansion": "ARR",
        "anchors": ["Silkscreen", "Deadly Thrust", "Sticky Web"]
    },
    "Haukke-Manor": {
        "type": "dungeon", "expansion": "ARR",
        "anchors": ["Void Fire III", "Dark Mist", "Void Thunder III", "Lady's Candle"]
    },
    "Brayflox-Longstop": {
        "type": "dungeon", "expansion": "ARR",
        "anchors": ["Dragon Breath", "Toxic Vomit", "Inflammable Fumes"]
    },
    "Stone-Vigil": {
        "type": "dungeon", "expansion": "ARR",
        "anchors": ["Sheet of Ice", "Cauterize", "Swinge"]
    },
    "Cutters-Cry": {
        "type": "dungeon", "expansion": "ARR",
        "anchors": ["Dragon's Voice", "Ram's Voice", "Cold Breath"]
    },
    "Dzemael-Darkhold": {
        "type": "dungeon", "expansion": "ARR",
        "anchors": ["Desolation", "Corrupted Crystal"]
    },
    "Aurum-Vale": {
        "type": "dungeon", "expansion": "ARR",
        "anchors": ["Glower", "100-tonze Swing", "100-tonze Swipe", "Eye of the Beholder"]
    },
    "Wanderers-Palace": {
        "type": "dungeon", "expansion": "ARR",
        "anchors": ["Everybody's Grudge", "Scourge of Nym"]
    },
    "Castrum-Meridianum": {
        "type": "dungeon", "expansion": "ARR",
        "anchors": ["Ceruleum Vent", "Magitek Cannon"]
    },
    "Praetorium": {
        "type": "dungeon", "expansion": "ARR",
        "anchors": ["Aetheroplasm", "Ultima"]
    },
    "Amdapor-Keep": {
        "type": "dungeon", "expansion": "ARR",
        "anchors": ["Repel", "Murder Hole", "Meteor"]
    },
    "Pharos-Sirius": {
        "type": "dungeon", "expansion": "ARR",
        "anchors": ["Lunatic Voice", "Song of Torment", "Acid Rain"]
    },
    "Copperbell-HM": {
        "type": "dungeon", "expansion": "ARR",
        "anchors": ["Plaincracker", "Crystal Needle"]
    },
    "Haukke-Manor-HM": {
        "type": "dungeon", "expansion": "ARR",
        "anchors": ["Void Fire IV", "Dark Mist", "Beguiling Mist"]
    },
    "Brayflox-HM": {
        "type": "dungeon", "expansion": "ARR",
        "anchors": ["Ceruleum Vent", "Self-destruct", "Gobmachine Thunderclap"]
    },
    "Halatali-HM": {
        "type": "dungeon", "expansion": "ARR",
        "anchors": ["Shockwave", "Ecliptic Meteor"]
    },
    "Lost-City-Amdapor": {
        "type": "dungeon", "expansion": "ARR",
        "anchors": ["Mega Holy", "Dark Arrivisme", "Shadow Flare", "Void Fire IV"]
    },
    "Hullbreaker-Isle": {
        "type": "dungeon", "expansion": "ARR",
        "anchors": ["Grand Slam", "Tidal Roar", "Hydroshot"]
    },
    "Tam-Tara-HM": {
        "type": "dungeon", "expansion": "ARR",
        "anchors": ["Void Fire II", "Meteor Impact", "Dark Mist"]
    },
    "Stone-Vigil-HM": {
        "type": "dungeon", "expansion": "ARR",
        "anchors": ["Swinge", "Sheet of Ice"]
    },
    "Snowcloak": {
        "type": "dungeon", "expansion": "ARR",
        "anchors": ["Heavensward Roar", "Lunar Cry", "Cold Wave"]
    },
    "Sastasha-HM": {
        "type": "dungeon", "expansion": "ARR",
        "anchors": ["Tail Screw", "Brine Breath", "Grotto Geyser"]
    },
    "Sunken-Temple-HM": {
        "type": "dungeon", "expansion": "ARR",
        "anchors": ["Doom of the Living", "Mow"]
    },
    "Wanderers-Palace-HM": {
        "type": "dungeon", "expansion": "ARR",
        "anchors": ["Massive Burst", "Scorched Earth"]
    },
    "Keeper-of-the-Lake": {
        "type": "dungeon", "expansion": "ARR",
        "anchors": ["Ceruleum Vent", "Magitek Ray", "Rotoswipe"]
    },
    "Amdapor-Keep-HM": {
        "type": "dungeon", "expansion": "ARR",
        "anchors": ["Mega Holy", "Dark Mist", "Shadow Eruption"]
    },

    # ========================================================================
    # HW DUNGEONS (3.x)
    # ========================================================================
    "Dusk-Vigil": {
        "type": "dungeon", "expansion": "HW",
        "anchors": ["Frozen Mist", "Cauterize", "Tombstone"]
    },
    "Sohm-Al": {
        "type": "dungeon", "expansion": "HW",
        "anchors": ["Deadly Thrust", "Fireball", "Levinbolt"]
    },
    "The-Aery": {
        "type": "dungeon", "expansion": "HW",
        "anchors": ["Absolute Zero", "Holy Breath", "Levinbolt", "Cauterize"]
    },
    "The-Vault": {
        "type": "dungeon", "expansion": "HW",
        "anchors": ["Holy Shield Bash", "Heavenly Slash", "Holiest of Holy", "Brightsphere", "Sacred Cross"]
    },
    "Great-Gubal-Library": {
        "type": "dungeon", "expansion": "HW",
        "anchors": ["Disclosure", "Deep Darkness", "Sea of Flames", "Frightful Roar"]
    },
    "Aetherochemical-Research": {
        "type": "dungeon", "expansion": "HW",
        "anchors": ["Blizzard Sphere", "Fire Sphere", "Height of Chaos", "Universal Manipulation", "Dark Orb"]
    },
    "Neverreap": {
        "type": "dungeon", "expansion": "HW",
        "anchors": ["Hot Charge", "Winding Current"]
    },
    "Fractal-Continuum": {
        "type": "dungeon", "expansion": "HW",
        "anchors": ["Seed of the Rivers", "Rapid Sever", "Sanctification"]
    },
    "Saint-Mociannes": {
        "type": "dungeon", "expansion": "HW",
        "anchors": ["Sap Shower", "Vine Probe"]
    },
    "Pharos-Sirius-HM": {
        "type": "dungeon", "expansion": "HW",
        "anchors": ["Ghastly Shriek", "Song of Torment"]
    },
    "Antitower": {
        "type": "dungeon", "expansion": "HW",
        "anchors": ["Mega Graviton", "Equilibrium", "Rod"]
    },
    "Lost-City-Amdapor-HM": {
        "type": "dungeon", "expansion": "HW",
        "anchors": ["Mega Holy", "Entropify"]
    },
    "Sohr-Khai": {
        "type": "dungeon", "expansion": "HW",
        "anchors": ["Ancient Circle", "Shockwave", "Death Sentence"]
    },
    "Hullbreaker-Isle-HM": {
        "type": "dungeon", "expansion": "HW",
        "anchors": ["Tidal Roar", "Hydroshot"]
    },
    "Xelphatol": {
        "type": "dungeon", "expansion": "HW",
        "anchors": ["On High", "Swiftfeather", "Wind Blast"]
    },
    "Great-Gubal-Library-HM": {
        "type": "dungeon", "expansion": "HW",
        "anchors": ["Triclip", "Ecliptic Meteor"]
    },
    "Sohm-Al-HM": {
        "type": "dungeon", "expansion": "HW",
        "anchors": ["Deadly Thrust", "Fireball", "Hot Charge"]
    },
    "Baelsars-Wall": {
        "type": "dungeon", "expansion": "HW",
        "anchors": ["Magitek Claw", "Magitek Ray", "Dynamic Sensory Jammer"]
    },

    # ========================================================================
    # SB DUNGEONS (4.x)
    # ========================================================================
    "Sirensong-Sea": {
        "type": "dungeon", "expansion": "SB",
        "anchors": ["Void Fire III", "Shadow Split", "Black Pain"]
    },
    "Shisui-Violet-Tides": {
        "type": "dungeon", "expansion": "SB",
        "anchors": ["Thick Fog", "Silken Spray", "Amethyst Light"]
    },
    "Bardams-Mettle": {
        "type": "dungeon", "expansion": "SB",
        "anchors": ["Travail", "Tremblor", "Bardam's Ring"]
    },
    "Doma-Castle": {
        "type": "dungeon", "expansion": "SB",
        "anchors": ["Hexadrone", "Magitek Missiles", "Doman Steel"]
    },
    "Castrum-Abania": {
        "type": "dungeon", "expansion": "SB",
        "anchors": ["Magitek Cannon", "Aetheroplasm", "Diffractive Laser"]
    },
    "Ala-Mhigo": {
        "type": "dungeon", "expansion": "SB",
        "anchors": ["Aetherochemical Grenado", "Art of the Storm", "Art of the Swell"]
    },
    "Kugane-Castle": {
        "type": "dungeon", "expansion": "SB",
        "anchors": ["Clearout", "Juji Shuriken", "Issen"]
    },
    "Temple-of-the-Fist": {
        "type": "dungeon", "expansion": "SB",
        "anchors": ["Fierce Storm", "Wide Blaster"]
    },
    "Drowned-City-Skalla": {
        "type": "dungeon", "expansion": "SB",
        "anchors": ["Torpedo", "Hydro Pull", "Protolithic Puncture"]
    },
    "Hells-Lid": {
        "type": "dungeon", "expansion": "SB",
        "anchors": ["Swoop", "Bomb Toss", "Plummet"]
    },
    "Fractal-Continuum-HM": {
        "type": "dungeon", "expansion": "SB",
        "anchors": ["Sanctification", "Seed of the Rivers"]
    },
    "Swallows-Compass": {
        "type": "dungeon", "expansion": "SB",
        "anchors": ["Tengu Yawn", "Bitter Barbs", "Short End"]
    },
    "The-Burn": {
        "type": "dungeon", "expansion": "SB",
        "anchors": ["Crystal Needle", "Head Butt", "Quake"]
    },
    "Saint-Mociannes-HM": {
        "type": "dungeon", "expansion": "SB",
        "anchors": ["Sap Shower", "Vine Probe"]
    },
    "Ghimlyt-Dark": {
        "type": "dungeon", "expansion": "SB",
        "anchors": ["Ceruleum Vent", "Magitek Ray", "Freezing Missile"]
    },

    # ========================================================================
    # ShB DUNGEONS (5.x)
    # ========================================================================
    "Holminster-Switch": {
        "type": "dungeon", "expansion": "ShB",
        "anchors": ["The Path of Light", "Thumbscrew", "Wooden Horse", "Heretic's Fork"]
    },
    "Dohn-Mheg": {
        "type": "dungeon", "expansion": "ShB",
        "anchors": ["Candy Cane", "Virtuosic Capriccio", "Funambulist's Fantasia"]
    },
    "Qitana-Ravel": {
        "type": "dungeon", "expansion": "ShB",
        "anchors": ["Stonefist", "Sun Toss", "Lozatl's Scorn", "Wrath of the Ronka"]
    },
    "Malikahs-Well": {
        "type": "dungeon", "expansion": "ShB",
        "anchors": ["Intestinal Crank", "Efface", "Head Toss", "Tremblor"]
    },
    "Mt-Gulg": {
        "type": "dungeon", "expansion": "ShB",
        "anchors": ["Cyclone Wing", "Sacrament of Penance", "Catechism"]
    },
    "Amaurot": {
        "type": "dungeon", "expansion": "ShB",
        "anchors": ["Meteor Rain", "Therion Charge", "Shadow Wreck", "Apokalypsis"]
    },
    "The-Twinning": {
        "type": "dungeon", "expansion": "ShB",
        "anchors": ["Augurium", "Rail Cannon", "Artificial Gravity"]
    },
    "Akademia-Anyder": {
        "type": "dungeon", "expansion": "ShB",
        "anchors": ["Lash", "Arbor Storm", "Extensible Tendrils"]
    },
    "Grand-Cosmos": {
        "type": "dungeon", "expansion": "ShB",
        "anchors": ["Tribulation", "Black Bolt", "Dark Well", "Otherworldly Heat"]
    },
    "Anamnesis-Anyder": {
        "type": "dungeon", "expansion": "ShB",
        "anchors": ["Fetid Fang", "Luminous Ray", "Inscrutability"]
    },
    "Heroes-Gauntlet": {
        "type": "dungeon", "expansion": "ShB",
        "anchors": ["Spectral Gust", "Spectral Dream", "Spectral Whirlwind"]
    },
    "Matoyas-Relict": {
        "type": "dungeon", "expansion": "ShB",
        "anchors": ["Crypt Dust", "Muddy Puddles", "Bog Bomb"]
    },
    "Paglthan": {
        "type": "dungeon", "expansion": "ShB",
        "anchors": ["Fireball", "Touchdown", "Spike Flail"]
    },

    # ========================================================================
    # EW DUNGEONS (6.x)
    # ========================================================================
    "Tower-of-Zot": {
        "type": "dungeon", "expansion": "EW",
        "anchors": ["Manusya Bio", "Manusya Blizzard III", "Manusya Fire III", "Manusya Thunder III", "Delta Attack", "Dhrupad"]
    },
    "Tower-of-Babil": {
        "type": "dungeon", "expansion": "EW",
        "anchors": ["Ground and Pound", "Magitek Chakram", "Magitek Explosive", "Magitek Ray"]
    },
    "Vanaspati": {
        "type": "dungeon", "expansion": "EW",
        "anchors": ["Note of Despair", "Gnaw", "Total Wreck"]
    },
    "Ktisis-Hyperboreia": {
        "type": "dungeon", "expansion": "EW",
        "anchors": ["Skull Dasher", "Frigid Stomp", "Hermetica"]
    },
    "Aitiascope": {
        "type": "dungeon", "expansion": "EW",
        "anchors": ["Scream of the Fallen", "Dark Flame"]
    },
    "Dead-Ends": {
        "type": "dungeon", "expansion": "EW",
        "anchors": ["Mega Holy", "Dead Star", "In Death, Life"]
    },
    "Smileton": {
        "type": "dungeon", "expansion": "EW",
        "anchors": ["Tempered Smite", "Uptown Funk", "Smiley Face"]
    },
    "Stigma-Dreamscape": {
        "type": "dungeon", "expansion": "EW",
        "anchors": ["Atomic Flame", "Rush", "Mustard Bomb"]
    },
    "Alzadaals-Legacy": {
        "type": "dungeon", "expansion": "EW",
        "anchors": ["Big Wave", "Billowing Bolts", "Bonebreaker"]
    },
    "Fell-Court-Troia": {
        "type": "dungeon", "expansion": "EW",
        "anchors": ["Void Gravity", "Antipodal Assault", "Beatific Scorn"]
    },
    "Lapis-Manalis": {
        "type": "dungeon", "expansion": "EW",
        "anchors": ["Frost Breath", "Albion's Embrace", "Icebreaker"]
    },
    "Aetherfont": {
        "type": "dungeon", "expansion": "EW",
        "anchors": ["Explosive Theorem", "Octoburst"]
    },
    "Lunar-Subterrane": {
        "type": "dungeon", "expansion": "EW",
        "anchors": ["Dark Impact", "Lunar Kiss", "Dark Eruption"]
    },

    # ========================================================================
    # DT DUNGEONS (7.x)
    # ========================================================================
    "Ihuykatumu": {
        "type": "dungeon", "expansion": "DT",
        "anchors": ["Decay", "Stone Flail"]
    },
    "Worqor-Zormor": {
        "type": "dungeon", "expansion": "DT",
        "anchors": ["Fluff Breeze", "Sparking Fissure"]
    },
    "Skydeep-Cenote": {
        "type": "dungeon", "expansion": "DT",
        "anchors": ["Land Wave", "Abyssal Tide"]
    },
    "Vanguard": {
        "type": "dungeon", "expansion": "DT",
        "anchors": ["Electrowave", "Enhanced Mobility"]
    },
    "Origenics": {
        "type": "dungeon", "expansion": "DT",
        "anchors": ["Electrolance", "Synthetic Blades", "Bio Bomb"]
    },
    "Alexandria": {
        "type": "dungeon", "expansion": "DT",
        "anchors": ["Supercharged Lasers", "Interference", "Overexposure"]
    },
    "Strayborough-Deadwalk": {
        "type": "dungeon", "expansion": "DT",
        "anchors": ["His Dark Majesty", "Falling Nightmare"]
    },
    "Tender-Valley": {
        "type": "dungeon", "expansion": "DT",
        "anchors": ["Twisting Dive", "Plummet"]
    },
    "Yuweyawata-Field-Station": {
        "type": "dungeon", "expansion": "DT",
        "anchors": ["Thunderstrike", "Electric Burst"]
    },

    # ========================================================================
    # ARR NORMAL TRIALS (2.x)
    # ========================================================================
    "Ifrit-Normal": {
        "type": "trial", "expansion": "ARR",
        "anchors": ["Hellfire", "Eruption", "Radiant Plume", "Incinerate", "Vulcan Burst"]
    },
    "Titan-Normal": {
        "type": "trial", "expansion": "ARR",
        "anchors": ["Earthen Fury", "Weight of the Land", "Geocrush", "Rock Buster", "Landslide", "Tumult"]
    },
    "Garuda-Normal": {
        "type": "trial", "expansion": "ARR",
        "anchors": ["Aerial Blast", "Mistral Song", "Slipstream", "Wicked Wheel", "Downburst"]
    },
    "Cape-Westwind": {
        "type": "trial", "expansion": "ARR",
        "anchors": ["Magitek Missiles"]
    },
    "Thornmarch": {
        "type": "trial", "expansion": "ARR",
        "anchors": ["Memento Moogle", "Pom Holy"]
    },
    "Steps-of-Faith": {
        "type": "trial", "expansion": "ARR",
        "anchors": ["Cauterize", "Fireball"]
    },
    "Chrysalis": {
        "type": "trial", "expansion": "ARR",
        "anchors": ["Blighted Bouquet", "Dark Eruption", "Spark of Darkness"]
    },

    # ========================================================================
    # HW NORMAL TRIALS (3.x)
    # ========================================================================
    "Bismarck-Normal": {
        "type": "trial", "expansion": "HW",
        "anchors": ["Breach Blast", "Sharp Gust"]
    },
    "Ravana-Normal": {
        "type": "trial", "expansion": "HW",
        "anchors": ["Bloody Fuller", "Blinding Blade", "Liberation", "Final Liberation"]
    },
    "Thordan-Normal": {
        "type": "trial", "expansion": "HW",
        "anchors": ["Ascalon's Might", "Lightning Storm", "Ancient Quaga", "Knights of the Round"]
    },
    "Sephirot-Normal": {
        "type": "trial", "expansion": "HW",
        "anchors": ["Ein Sof", "Chesed", "Pillar of Mercy", "Fiendish Rage"]
    },
    "Nidhogg-Normal": {
        "type": "trial", "expansion": "HW",
        "anchors": ["Akh Morn", "Hot Wing", "Hot Tail", "Cauterize"]
    },
    "Sophia-Normal": {
        "type": "trial", "expansion": "HW",
        "anchors": ["Thunder II", "Thunder III", "Aero III", "Cintamani", "Execute"]
    },
    "Zurvan-Normal": {
        "type": "trial", "expansion": "HW",
        "anchors": ["Flare Star", "Southern Cross", "Tail End", "Ahura Mazda"]
    },

    # ========================================================================
    # SB NORMAL TRIALS (4.x)
    # ========================================================================
    "Susano-Normal": {
        "type": "trial", "expansion": "SB",
        "anchors": ["Assail", "Rasen Kaikyo", "Ukehi", "Stormsplitter"]
    },
    "Lakshmi-Normal": {
        "type": "trial", "expansion": "SB",
        "anchors": ["Pull of Light", "Stotram", "The Pall of Light", "Alluring Arm"]
    },
    "Shinryu-Normal": {
        "type": "trial", "expansion": "SB",
        "anchors": ["Tidal Wave", "Protostar", "Akh Morn", "Dark Matter"]
    },
    "Tsukuyomi-Normal": {
        "type": "trial", "expansion": "SB",
        "anchors": ["Reprimand", "Nightfall", "Nightbloom"]
    },
    "Byakko-Normal": {
        "type": "trial", "expansion": "SB",
        "anchors": ["Storm Pulse", "Heavenly Strike", "State of Shock"]
    },
    "Suzaku-Normal": {
        "type": "trial", "expansion": "SB",
        "anchors": ["Screams of the Damned", "Scarlet Fever"]
    },
    "Seiryu-Normal": {
        "type": "trial", "expansion": "SB",
        "anchors": ["Serpent Ascending", "Coursing River", "Forbidden Arts"]
    },

    # ========================================================================
    # ShB NORMAL TRIALS (5.x)
    # ========================================================================
    "Titania-Normal": {
        "type": "trial", "expansion": "ShB",
        "anchors": ["Bright Sabbath", "Phantom Rune", "Mist Rune", "Being Mortal"]
    },
    "Innocence-Normal": {
        "type": "trial", "expansion": "ShB",
        "anchors": ["Shadowreaver", "Righteous Bolt", "Daybreak"]
    },
    "Hades-Normal": {
        "type": "trial", "expansion": "ShB",
        "anchors": ["Ravenous Assault", "Shadow Spread", "Bad Faith", "Gigantomachy"]
    },
    "WoL-Normal": {
        "type": "trial", "expansion": "ShB",
        "anchors": ["Terror Unleashed", "Absolute Holy", "Elddragon Dive"]
    },
    "Ruby-Weapon-Normal": {
        "type": "trial", "expansion": "ShB",
        "anchors": ["Optimized Ultima", "Stamp", "Ruby Ray"]
    },
    "Emerald-Weapon-Normal": {
        "type": "trial", "expansion": "ShB",
        "anchors": ["Emerald Beam", "Divide Et Impera", "Optimized Ultima"]
    },
    "Diamond-Weapon-Normal": {
        "type": "trial", "expansion": "ShB",
        "anchors": ["Diamond Rain", "Adamant Purge", "Photon Burst"]
    },

    # ========================================================================
    # EW NORMAL TRIALS (6.x)
    # ========================================================================
    "Zodiark-Normal": {
        "type": "trial", "expansion": "EW",
        "anchors": ["Kokytos", "Styx", "Ania", "Exoterikos"]
    },
    "Hydaelyn-Normal": {
        "type": "trial", "expansion": "EW",
        "anchors": ["Heros's Radiance", "Mousa's Scorn", "Highest Holy", "Crystallize"]
    },
    "Endsinger-Normal": {
        "type": "trial", "expansion": "EW",
        "anchors": ["Elegeia Unforgotten", "Elegeia", "Hubris", "Telos"]
    },
    "Barbariccia-Normal": {
        "type": "trial", "expansion": "EW",
        "anchors": ["Void Aero IV", "Savage Barbery", "Knuckle Drum"]
    },
    "Rubicante-Normal": {
        "type": "trial", "expansion": "EW",
        "anchors": ["Inferno", "Shattering Heat", "Blazing Rapture"]
    },
    "Golbez-Normal": {
        "type": "trial", "expansion": "EW",
        "anchors": ["Terrastorm", "Black Fang", "Binding Cold", "Void Meteor"]
    },
    "Zeromus-Normal": {
        "type": "trial", "expansion": "EW",
        "anchors": ["Abyssal Nox", "Sable Thread", "Big Bang", "Flare"]
    },

    # ========================================================================
    # DT NORMAL TRIALS (7.x)
    # ========================================================================
    "Valigarmanda-Normal": {
        "type": "trial", "expansion": "DT",
        "anchors": ["Susurrant Breath", "Skyruin", "Hail of Feathers"]
    },
    "ZoraalJa-Normal": {
        "type": "trial", "expansion": "DT",
        "anchors": ["Dawn of an Age", "Actualize", "Regicidal Rage"]
    },
    "Sphene-Normal": {
        "type": "trial", "expansion": "DT",
        "anchors": ["Coronation", "Prosecution of War", "Absolute Authority"]
    },

    # ========================================================================
    # ARR NORMAL RAIDS - COILS (2.x)
    # ========================================================================
    "T1N-Caduceus": {
        "type": "normal_raid", "expansion": "ARR",
        "anchors": ["Hood Swing", "Regorge", "Steel Scales"]
    },
    "T2N-ADS": {
        "type": "normal_raid", "expansion": "ARR",
        "anchors": ["High Voltage", "Repelling Cannons"]
    },
    "T4-Dreadnought": {
        "type": "normal_raid", "expansion": "ARR",
        "anchors": ["Rotoswipe", "Gravity Thrust"]
    },

    # ========================================================================
    # HW NORMAL RAIDS - ALEXANDER (3.x)
    # ========================================================================
    "A1N-Oppressor": {
        "type": "normal_raid", "expansion": "HW",
        "anchors": ["Hydrothermal Missile", "Photon Spaser", "Resin Bomb"]
    },
    "A2N-Gobwalker": {
        "type": "normal_raid", "expansion": "HW",
        "anchors": ["Bomb's Away", "Gobstraight"]
    },
    "A3N-LivingLiquid": {
        "type": "normal_raid", "expansion": "HW",
        "anchors": ["Cascade", "Protean Wave", "Sluice", "Splash"]
    },
    "A4N-Manipulator": {
        "type": "normal_raid", "expansion": "HW",
        "anchors": ["Mortal Revolution", "Perpetual Ray", "Carnage Zero"]
    },
    "A5N-Faust": {
        "type": "normal_raid", "expansion": "HW",
        "anchors": ["Boost", "Shock Therapy"]
    },
    "A6N-MultiPhase": {
        "type": "normal_raid", "expansion": "HW",
        "anchors": ["Enumeration", "Ultra Flash", "Mega Beam"]
    },
    "A7N-Quickthinx": {
        "type": "normal_raid", "expansion": "HW",
        "anchors": ["Sizzlespark", "Sizzlebeam"]
    },
    "A8N-BruteJustice": {
        "type": "normal_raid", "expansion": "HW",
        "anchors": ["Apocalyptic Ray", "Super Jump", "J Storm"]
    },
    "A9N-Refurbisher": {
        "type": "normal_raid", "expansion": "HW",
        "anchors": ["Scrapline", "Stockpile"]
    },
    "A10N-Lamebrix": {
        "type": "normal_raid", "expansion": "HW",
        "anchors": ["Goblin Rush", "Gobrush Rushgob"]
    },
    "A11N-CruiseChaser": {
        "type": "normal_raid", "expansion": "HW",
        "anchors": ["Optical Sight", "Whirlwind", "Photon"]
    },
    "A12N-AlexPrime": {
        "type": "normal_raid", "expansion": "HW",
        "anchors": ["Mega Holy", "Gravitational Anomaly", "Sacrament", "Divine Spear"]
    },

    # ========================================================================
    # SB NORMAL RAIDS - OMEGA (4.x)
    # ========================================================================
    "O1N-AlteRoite": {
        "type": "normal_raid", "expansion": "SB",
        "anchors": ["Wyrm Tail", "Twin Bolt", "Charybdis", "Roar"]
    },
    "O2N-Catastrophe": {
        "type": "normal_raid", "expansion": "SB",
        "anchors": ["Gravitational Wave", "Earthquake", "Antilight"]
    },
    "O3N-Halicarnassus": {
        "type": "normal_raid", "expansion": "SB",
        "anchors": ["The Queen's Waltz", "Ribbit", "The Playing Field"]
    },
    "O4N-Exdeath": {
        "type": "normal_raid", "expansion": "SB",
        "anchors": ["Vacuum Wave", "Black Hole", "Delta Attack", "Flare"]
    },
    "O5N-PhantomTrain": {
        "type": "normal_raid", "expansion": "SB",
        "anchors": ["Diabolical Whistle", "Doom Strike", "Head On"]
    },
    "O6N-Chadarnook": {
        "type": "normal_raid", "expansion": "SB",
        "anchors": ["Flash Fire", "Demonic Stone", "Poltergeist"]
    },
    "O7N-Guardian": {
        "type": "normal_raid", "expansion": "SB",
        "anchors": ["Atomic Ray", "Arm and Hammer", "Magitek Ray"]
    },
    "O8N-Kefka": {
        "type": "normal_raid", "expansion": "SB",
        "anchors": ["Hyperdrive", "Light of Judgment", "Thrumming Thunder"]
    },
    "O9N-Chaos": {
        "type": "normal_raid", "expansion": "SB",
        "anchors": ["Chaotic Dispersion", "Bowels of Agony", "Blaze", "Tsunami"]
    },
    "O10N-Midgardsormr": {
        "type": "normal_raid", "expansion": "SB",
        "anchors": ["Protostar", "Thunderstorm", "Tail End"]
    },
    "O11N-OmegaMF": {
        "type": "normal_raid", "expansion": "SB",
        "anchors": ["Ion Efflux", "Mustard Bomb", "Laser Shower"]
    },
    "O12N-Omega": {
        "type": "normal_raid", "expansion": "SB",
        "anchors": ["Cosmo Memory", "Patch", "Archive Peripheral"]
    },

    # ========================================================================
    # ShB NORMAL RAIDS - EDEN (5.x)
    # ========================================================================
    "E1N-EdenPrime": {
        "type": "normal_raid", "expansion": "ShB",
        "anchors": ["Eden's Gravity", "Spear of Paradise", "Vice and Virtue"]
    },
    "E2N-Voidwalker": {
        "type": "normal_raid", "expansion": "ShB",
        "anchors": ["Shadowflame", "Entropy", "Punishing Ray"]
    },
    "E3N-Leviathan": {
        "type": "normal_raid", "expansion": "ShB",
        "anchors": ["Tidal Wave", "Temporary Current", "Tidal Rage"]
    },
    "E4N-Titan": {
        "type": "normal_raid", "expansion": "ShB",
        "anchors": ["Stonecrusher", "Weight of the Land", "Voice of the Land", "Geocrush"]
    },
    "E5N-Ramuh": {
        "type": "normal_raid", "expansion": "ShB",
        "anchors": ["Judgment Volts", "Crippling Blow", "Stepped Leader"]
    },
    "E6N-GarudaIfrit": {
        "type": "normal_raid", "expansion": "ShB",
        "anchors": ["Hands of Hell", "Instant Incineration", "Vacuum Slice"]
    },
    "E7N-IdolOfDarkness": {
        "type": "normal_raid", "expansion": "ShB",
        "anchors": ["Empty Wave", "Words of Night"]
    },
    "E8N-Shiva": {
        "type": "normal_raid", "expansion": "ShB",
        "anchors": ["Diamond Frost", "Heavenly Strike", "Absolute Zero"]
    },
    "E9N-CloudOfDarkness": {
        "type": "normal_raid", "expansion": "ShB",
        "anchors": ["Ground-razing Particle Beam", "Zero-form Particle Beam"]
    },
    "E10N-Shadowkeeper": {
        "type": "normal_raid", "expansion": "ShB",
        "anchors": ["Deepshadow Nova", "Shadow's Edge", "Giga Slash"]
    },
    "E11N-Fatebreaker": {
        "type": "normal_raid", "expansion": "ShB",
        "anchors": ["Burnt Strike", "Bound of Faith", "Burnished Glory"]
    },
    "E12N-EdenPromise": {
        "type": "normal_raid", "expansion": "ShB",
        "anchors": ["Maleficium", "Shockwave Pulsar", "Diamond Dust"]
    },

    # ========================================================================
    # EW NORMAL RAIDS - PANDAEMONIUM (6.x)
    # ========================================================================
    "P1N-Erichthonios": {
        "type": "normal_raid", "expansion": "EW",
        "anchors": ["Warder's Wrath", "Heavy Hand", "Pitiless Flail", "Shining Cells"]
    },
    "P2N-Hippokampos": {
        "type": "normal_raid", "expansion": "EW",
        "anchors": ["Sewage Deluge", "Murky Depths", "Coherence", "Shockwave"]
    },
    "P3N-Phoinix": {
        "type": "normal_raid", "expansion": "EW",
        "anchors": ["Experimental Fireplume", "Heat of Condemnation", "Dead Rebirth"]
    },
    "P4N-Hesperos": {
        "type": "normal_raid", "expansion": "EW",
        "anchors": ["Decollation", "Elegant Evisceration", "Bloodrake", "Pinax"]
    },
    "P5N-ProtoCarbuncle": {
        "type": "normal_raid", "expansion": "EW",
        "anchors": ["Ruby Glow", "Sonic Howl", "Topaz Cluster"]
    },
    "P6N-Hegemone": {
        "type": "normal_raid", "expansion": "EW",
        "anchors": ["Hemitheos's Dark IV", "Choros Ixou", "Synergy"]
    },
    "P7N-Agdistis": {
        "type": "normal_raid", "expansion": "EW",
        "anchors": ["Bough of Attis", "Spark of Life", "Forbidden Fruit"]
    },
    "P8N-Hephaistos": {
        "type": "normal_raid", "expansion": "EW",
        "anchors": ["Genesis of Flame", "Volcanic Torches", "Flameviper"]
    },
    "P9N-Kokytos": {
        "type": "normal_raid", "expansion": "EW",
        "anchors": ["Gluttony's Augur", "Ravening", "Ascendant Fist"]
    },
    "P10N-Pandaemonium": {
        "type": "normal_raid", "expansion": "EW",
        "anchors": ["Pandaemoniac Meltdown", "Soul Grasp", "Wicked Step"]
    },
    "P11N-Themis": {
        "type": "normal_raid", "expansion": "EW",
        "anchors": ["Eunomia", "Jury Overruling", "Upheld Overruling"]
    },
    "P12N-Athena": {
        "type": "normal_raid", "expansion": "EW",
        "anchors": ["On the Soul", "Glaukopis", "Trinity of Souls", "Ultima"]
    },

    # ========================================================================
    # DT NORMAL RAIDS - AAC (7.x)
    # ========================================================================
    "M1N-BlackCat": {
        "type": "normal_raid", "expansion": "DT",
        "anchors": ["Mouser", "Biscuit Maker", "Bloody Scratch", "One-two Paw"]
    },
    "M2N-HoneyBLovely": {
        "type": "normal_raid", "expansion": "DT",
        "anchors": ["Call Me Honey", "Bee Sting", "Honey Beeline", "Blinding Love"]
    },
    "M3N-BruteBomber": {
        "type": "normal_raid", "expansion": "DT",
        "anchors": ["Brutish Swing", "Knuckle Sandwich", "Octuple Lariat"]
    },
    "M4N-WickedThunder": {
        "type": "normal_raid", "expansion": "DT",
        "anchors": ["Wicked Thunder", "Wrath of Zeus", "Wicked Bolt"]
    },
    "M5N": {
        "type": "normal_raid", "expansion": "DT",
        "anchors": ["Frosting Fracas", "Chill Cauldron"]
    },
    "M6N": {
        "type": "normal_raid", "expansion": "DT",
        "anchors": ["Mousse Mural", "Pâtissière's Art"]
    },
    "M7N": {
        "type": "normal_raid", "expansion": "DT",
        "anchors": ["Chilling Chirp", "Peck and Poison"]
    },
    "M8N": {
        "type": "normal_raid", "expansion": "DT",
        "anchors": ["Extraplanar Pursuit", "Howling Blade"]
    },
    "M9N": {
        "type": "normal_raid", "expansion": "DT",
        "anchors": ["Crown of Arcadia", "Charybdistopia"]
    },
    "M10N": {
        "type": "normal_raid", "expansion": "DT",
        "anchors": ["Overrun", "Trample"]
    },
    "M11N": {
        "type": "normal_raid", "expansion": "DT",
        "anchors": ["Dance of Domination", "Meteorain", "Flatliner"]
    },
    "M12N": {
        "type": "normal_raid", "expansion": "DT",
        "anchors": ["The Fixer", "Splattershed", "Unmitigated Explosion"]
    },

    # ========================================================================
    # ARR ALLIANCE RAIDS - CRYSTAL TOWER (2.x)
    # ========================================================================
    "Labyrinth-of-Ancients": {
        "type": "alliance", "expansion": "ARR",
        "anchors": ["Ancient Flare", "Ballistic Missile", "Iron Giant Swing", "Curse of the Mummy"]
    },
    "Syrcus-Tower": {
        "type": "alliance", "expansion": "ARR",
        "anchors": ["Ancient Quaga", "Curtain Call", "Shockwave", "Daybreak"]
    },
    "World-of-Darkness": {
        "type": "alliance", "expansion": "ARR",
        "anchors": ["Flood of Darkness", "Mega Death", "Flare Star", "Particle Beam"]
    },

    # ========================================================================
    # HW ALLIANCE RAIDS - VOID ARK (3.x)
    # ========================================================================
    "Void-Ark": {
        "type": "alliance", "expansion": "HW",
        "anchors": ["Mega Death", "Mortal Revolution", "Body Slam"]
    },
    "Weeping-City-of-Mhach": {
        "type": "alliance", "expansion": "HW",
        "anchors": ["Materialize", "Shadow Burst", "Dark Eruption", "Flare Star"]
    },
    "Dun-Scaith": {
        "type": "alliance", "expansion": "HW",
        "anchors": ["Fire IV", "Blizzard IV", "Scathach's Connla", "Shadow Links"]
    },

    # ========================================================================
    # SB ALLIANCE RAIDS - IVALICE (4.x)
    # ========================================================================
    "Royal-City-of-Rabanastre": {
        "type": "alliance", "expansion": "SB",
        "anchors": ["Command Tower", "Landwaster", "Crush Helm", "Divine Judgment"]
    },
    "Ridorana-Lighthouse": {
        "type": "alliance", "expansion": "SB",
        "anchors": ["Shockwave", "Tsunami", "Solar Storm", "Construct Destroy"]
    },
    "Orbonne-Monastery": {
        "type": "alliance", "expansion": "SB",
        "anchors": ["Crush Weapon", "Hallowed Bolt", "Ultima", "Shadowblade", "T.G. Holy Sword"]
    },

    # ========================================================================
    # ShB ALLIANCE RAIDS - YORHA (5.x)
    # ========================================================================
    "Copied-Factory": {
        "type": "alliance", "expansion": "ShB",
        "anchors": ["Clanging Blow", "Energy Assault", "Laser Turret", "Total Annihilation Maneuver"]
    },
    "Puppets-Bunker": {
        "type": "alliance", "expansion": "ShB",
        "anchors": ["Centrifugal Slice", "Energy Barrage", "Laser Shower", "Fire All Weapons"]
    },
    "Tower-at-Paradigms-Breach": {
        "type": "alliance", "expansion": "ShB",
        "anchors": ["Anti-personnel Missile", "Shockwave", "Guided Missile", "Diffuse Energy"]
    },

    # ========================================================================
    # EW ALLIANCE RAIDS - MYTHS OF THE REALM (6.x)
    # ========================================================================
    "Aglaia": {
        "type": "alliance", "expansion": "EW",
        "anchors": ["Byregot's Strike", "Levinforge", "Rhalgr's Beacon", "Destructive Bolt", "Lightning Reign", "Hand of the Destroyer"]
    },
    "Euphrosyne": {
        "type": "alliance", "expansion": "EW",
        "anchors": ["Quintessence", "Love's Light", "Matron's Breath", "Hydrostasis", "Spring Crystal"]
    },
    "Thaleia": {
        "type": "alliance", "expansion": "EW",
        "anchors": ["Rheognosis", "Geocentrism", "Glaukopis", "Hieroglyphika", "Whorl of the Mind"]
    },

    # ========================================================================
    # DT ALLIANCE RAIDS (7.x)
    # ========================================================================
    "Jeuno-First-Walk": {
        "type": "alliance", "expansion": "DT",
        "anchors": ["Scrapline", "Megalithe", "Banishga IV", "Provenance Watcher"]
    },

    # ========================================================================
    # EW VARIANT DUNGEONS (6.x)
    # ========================================================================
    "Variant-Sildihn-Subterrane": {
        "type": "variant", "expansion": "EW",
        "anchors": ["Puff and Tumble", "Slippery Soap", "Fizzling Suds", "Rush of Might",
                     "Sculptor's Passion", "Show of Strength", "Infern Brand", "Cast Shadow",
                     "Firesteel Fracture", "Blessed Beacon"]
    },
    "Variant-Mount-Rokkon": {
        "type": "variant", "expansion": "EW",
        "anchors": ["Splitting Cry", "Noble Pursuit", "Enkyo", "Unenlightenment",
                     "Humble Hammer", "Scarlet Auspice", "Kenki Release",
                     "Double Kasumi-giri", "Moonless Night", "Iai-kasumi-giri",
                     "Clearout", "Lateral Slice"]
    },
    "Variant-Aloalo-Island": {
        "type": "variant", "expansion": "EW",
        "anchors": ["Arcane Blight", "Tornado", "Made Magic", "Spring Crystals",
                     "Bubble Net", "Fluke Typhoon", "Hydrobomb", "Arcane Plot",
                     "Hundred Lashings", "Wood Golem", "Analysis"]
    },

    # ========================================================================
    # EW CRITERION DUNGEONS (6.x)
    # ========================================================================
    "Criterion-Sildihn": {
        "type": "criterion", "expansion": "EW",
        "anchors": ["Puff and Tumble", "Slippery Soap", "Fizzling Suds", "Rush of Might",
                     "Sculptor's Passion", "Infern Brand", "Cast Shadow",
                     "Firesteel Fracture", "Blessed Beacon", "Branding Flare"]
    },
    "Criterion-Sildihn-Savage": {
        "type": "criterion_savage", "expansion": "EW",
        "anchors": ["Puff and Tumble", "Slippery Soap", "Rush of Might",
                     "Sculptor's Passion", "Infern Brand", "Cast Shadow",
                     "Firesteel Fracture", "Blessed Beacon", "Branding Flare"]
    },
    "Criterion-Mount-Rokkon": {
        "type": "criterion", "expansion": "EW",
        "anchors": ["Splitting Cry", "Noble Pursuit", "Enkyo", "Unenlightenment",
                     "Humble Hammer", "Scarlet Auspice", "Kenki Release",
                     "Double Kasumi-giri", "Moonless Night", "Clearout"]
    },
    "Criterion-Mount-Rokkon-Savage": {
        "type": "criterion_savage", "expansion": "EW",
        "anchors": ["Splitting Cry", "Noble Pursuit", "Enkyo", "Unenlightenment",
                     "Humble Hammer", "Scarlet Auspice", "Kenki Release",
                     "Double Kasumi-giri", "Moonless Night", "Clearout"]
    },
    "Criterion-Aloalo": {
        "type": "criterion", "expansion": "EW",
        "anchors": ["Arcane Blight", "Tornado", "Spring Crystals", "Bubble Net",
                     "Fluke Typhoon", "Hydrobomb", "Arcane Plot", "Analysis",
                     "Hundred Lashings"]
    },
    "Criterion-Aloalo-Savage": {
        "type": "criterion_savage", "expansion": "EW",
        "anchors": ["Arcane Blight", "Tornado", "Spring Crystals", "Bubble Net",
                     "Fluke Typhoon", "Hydrobomb", "Arcane Plot", "Analysis",
                     "Hundred Lashings"]
    },

    # ========================================================================
    # DT CHAOTIC RAIDS (7.x)
    # ========================================================================
    "Chaotic-CloudOfDarkness": {
        "type": "chaotic", "expansion": "DT",
        "anchors": ["Doom Arc", "Zero-form Particle Beam", "Ground-razing Particle Beam",
                     "Wide-angle Particle Beam", "Flood of Darkness", "Curse of Darkness",
                     "Rapid-sequence Particle Beam", "Active-pivot Particle Beam",
                     "Ghastly Gloom", "Death's Embrace"]
    },

    # ========================================================================
    # DEEP DUNGEONS
    # ========================================================================
    "PotD-Floor50-Edda": {
        "type": "deep_dungeon", "expansion": "HW",
        "anchors": ["Black Honeymoon", "Void Fire III", "Cold Feet", "In Health"]
    },
    "PotD-Floor100-Nybeth": {
        "type": "deep_dungeon", "expansion": "HW",
        "anchors": ["Shackled Fist", "Abyss", "Doom of the Living", "Word of Pain"]
    },
    "PotD-Floor150-Edda2": {
        "type": "deep_dungeon", "expansion": "HW",
        "anchors": ["Black Honeymoon", "Cold Feet", "In Health"]
    },
    "PotD-Floor200-TheGodfather": {
        "type": "deep_dungeon", "expansion": "HW",
        "anchors": ["Grim Fate", "Grim Halo", "Black Nebula", "Big Burst", "Charybdis"]
    },
    "HoH-Floor30-Hiruko": {
        "type": "deep_dungeon", "expansion": "SB",
        "anchors": ["Supercell", "Charge", "Lightning Bolt", "Superstorm"]
    },
    "HoH-Floor100-Onra": {
        "type": "deep_dungeon", "expansion": "SB",
        "anchors": ["Ancient Quaga", "Burning Chains", "Lateral Slice"]
    },
    "EO-Floor30-Gancanagh": {
        "type": "deep_dungeon", "expansion": "EW",
        "anchors": ["Mandrastorm", "Caterwaul", "Authoritative Shriek"]
    },
    "EO-Floor100-ProtoKaliya": {
        "type": "deep_dungeon", "expansion": "EW",
        "anchors": ["Nerve Gas", "Barofield", "Resonance", "Main Head", "Auto-cannons"]
    },

    # ========================================================================
    # BOZJA / EUREKA FIELD OPERATIONS
    # ========================================================================
    "Castrum-Lacus-Litore": {
        "type": "field_operation", "expansion": "ShB",
        "anchors": ["Magitek Missiles", "Magitek Cannon", "Aetheroplasm",
                     "Baleful Comet", "Iron Splitter", "Magitek Magnetism"]
    },
    "Delubrum-Reginae": {
        "type": "field_operation", "expansion": "ShB",
        "anchors": ["Baleful Blade", "Fury of Bozja", "Queen's Shot",
                     "Above Board", "Lots Cast", "Heaven's Wrath",
                     "Optimal Play", "Pawn Off", "Beck and Call to Arms",
                     "Gods Save the Queen", "Relentless Play", "Judgment Blade"]
    },
    "Delubrum-Reginae-Savage": {
        "type": "field_operation_savage", "expansion": "ShB",
        "anchors": ["Baleful Blade", "Fury of Bozja", "Queen's Shot",
                     "Above Board", "Lots Cast", "Heaven's Wrath",
                     "Optimal Play", "Pawn Off", "Beck and Call to Arms",
                     "Gods Save the Queen", "Relentless Play", "Judgment Blade"]
    },
    "The-Dalriada": {
        "type": "field_operation", "expansion": "ShB",
        "anchors": ["Anti-personnel Missile", "Analysis", "Suppressive Magitek Rays",
                     "Magitek Halo", "Magitek Crossray", "Read Orders",
                     "Turbine", "Magitek Explosion", "Surface Missile"]
    },
    "Baldesion-Arsenal": {
        "type": "field_operation", "expansion": "SB",
        "anchors": ["Shockwave", "Art of the Swell", "Trounce", "Raiden",
                     "For Honor", "Cloud to Ground", "Gallop", "Streak Lightning"]
    },
    # NOTE: Add future DT content here as patches release (7.3+ alliance raids,
    # new EX trials, dungeons, variant/criterion dungeons, chaotic raids)
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
# SEMANTIC MECHANIC TAGGING
# ============================================================================
# Maps lowercase action names to semantic tags.
# Tags: "tankbuster", "raidwide", "stack", "prey", "marker" (spread/defamation)

KNOWN_TANKBUSTERS = {
    # ARR
    "death sentence", "flatten", "ravensbeak", "critical rip", "secondary head",
    "nerve gas", "revelation",
    # HW
    "hypercompressed plasma", "perpetual ray", "single buster", "gobhook",
    "spin crusher",
    # SB
    "arm and hammer", "hyperdrive", "chaotic dispersion", "stormsplitter",
    "tail end", "doom strike",
    # ShB
    "spear of paradise", "stonecrusher", "crippling blow", "instant incineration",
    "double slap", "umbra smash", "ravenous assault", "dual strike",
    "stamp", "auri arts", "vertical cleave", "scarlet price",
    # EW
    "warder's wrath", "pitiless flail", "heavy hand", "murky depths",
    "heat of condemnation", "decollation", "elegant evisceration",
    "flameviper", "glaukopis", "palladian grasp", "crush helm",
    "ascendant fist", "archaic demolish", "soul grasp", "wicked step",
    "ania", "hubris", "shattering heat", "dualfire",
    "mousa's scorn", "heros's sundering",
    # DT
    "biscuit maker", "predaceous pounce", "bee sting", "drop of venom",
    "poison sting", "knuckle sandwich", "doping draught", "wicked bolt",
    "electrope edge", "bitter whirlwind",
    # Ultimates
    "quadruple slap", "shell crusher", "black halo", "somber dance",
    "heavenly heel", "gnash and lash", "lash and gnash", "solar ray",
    "pile pitch", "darkdragon dive",
    # Generic patterns
    "tankbuster", "tank buster",
}

KNOWN_RAIDWIDES = {
    # ARR
    "gigaflare", "teraflare", "hellfire", "aerial blast", "earthen fury",
    "judgment bolt", "tidal wave", "aetheric profusion", "megaflare",
    "ancient flare", "mega death", "curtain call",
    # HW
    "mega holy", "whirlwind", "j wave", "ultimate end",
    # SB
    "almagest", "light of judgment", "forsaken", "laser shower",
    "ion efflux", "cosmo memory", "bowels of agony", "gravitational wave",
    "screams of the damned", "scarlet fever", "storm pulse", "nightbloom",
    "ahura mazda",
    # ShB
    "absolute zero", "diamond dust", "gigantomachy", "shadowreaver",
    "daybreak", "diamond rain", "optimized ultima", "flood of darkness",
    "bright sabbath", "being mortal", "total annihilation maneuver",
    "burnished glory", "titanomachy",
    # EW
    "sewage deluge", "dead rebirth", "searing stream", "sonic howl",
    "hemitheos's dark iv", "spark of life", "genesis of flame",
    "gluttony's augur", "harrowing hell", "eunomia", "on the soul",
    "kokytos", "styx", "highest holy", "radiant halo",
    "elegeia", "elegeia unforgotten", "void aero iv", "inferno",
    "blazing rapture", "terrastorm", "abyssal nox", "big bang",
    "knuckle drum",
    # DT
    "wrath of zeus", "call me honey", "dawn of an age", "skyruin",
    "calamitous cry", "bloody scratch", "soulshock", "stampeding thunder",
    "coronation", "royal domain", "aethertithe", "absolute authority",
    "prosecution of war", "regicidal rage", "actualize",
    "doom arc", "shockwave pulsar",
    # Ultimates
    "cyclonic break", "memory's end", "exaflare",
    # Generic patterns
    "raidwide",
}

KNOWN_STACKS = {
    # ARR
    "thermionic beam", "wild charge", "fireball",
    # HW
    "akh morn", "enumeration", "fiendish rage", "compressed lightning",
    "compressed water",
    # SB
    "the pall of light", "path of light", "stotram",
    "flaming crush", "megaflare",
    # ShB
    "morn afah", "dark eruption", "light rampant",
    "coherence",
    # EW
    "soul grasp", "banish iii", "hallowed ray",
    "double meteor", "eventide fall",
    # DT
    "wicked bolt",
    # Ultimates
    "irresistible grace", "cauterize",
    # Generic patterns
    "stack", "shared buster", "shared tankbuster",
}

KNOWN_PREYS = {
    # Named "Prey" mechanics
    "prey",
    # Homing / targeted attacks
    "homing lasers", "homing missile", "high-powered homing lasers",
    "ballistic missile",
    # Targeted AoE puddles / chasers
    "earth shaker", "meteor stream", "liquid hell", "feather rain",
    "divebomb", "eruption", "weight of the land", "searing wind",
    "dark fire iii", "dark blizzard iii",
    "levinbolt",  # individual targeted
    "thunderstorm",  # targeted circles
    # EW/DT targeted
    "flare star", "acid rain",
}

KNOWN_MARKERS = {
    # Spread markers
    "shadow spread", "unholy darkness", "spirit taker",
    "thunder iii", "dark aero iii",
    "scattered magic", "defamation",
    # Look-away markers
    "shadoweye", "cursed voice", "cursed shriek", "petrifaction",
    "demon eye",
    # Proximity markers
    "proximity",
    # Dorito / chase markers
    "flare",  # individual spread away
    # Generic
    "spread", "marker",
}

# Keyword-based heuristic patterns (applied when no exact match found)
TANKBUSTER_KEYWORDS = {"buster", "crusher", "cleave"}
STACK_KEYWORDS = {"stack"}


def tag_action(action):
    """Return a list of semantic tags for an action based on name and properties."""
    name = action.get("name", "").strip().lower()
    ct = action.get("cast_type", 0)
    er = action.get("effect_range", 0)
    cast_time = action.get("cast_time_s", 0)
    tags = []

    # Exact name matches
    if name in KNOWN_TANKBUSTERS:
        tags.append("tankbuster")
    if name in KNOWN_RAIDWIDES:
        tags.append("raidwide")
    if name in KNOWN_STACKS:
        tags.append("stack")
    if name in KNOWN_PREYS:
        tags.append("prey")
    if name in KNOWN_MARKERS:
        tags.append("marker")

    # Keyword heuristics (only if no exact match yet)
    if not tags:
        for kw in TANKBUSTER_KEYWORDS:
            if kw in name:
                tags.append("tankbuster")
                break
        for kw in STACK_KEYWORDS:
            if kw in name:
                tags.append("stack")
                break

    # Geometric heuristic: single-target with cast time => likely tankbuster
    if ct == 1 and cast_time >= 2.0 and "tankbuster" not in tags:
        tags.append("tankbuster")

    # Geometric heuristic: very large AoE => likely raidwide
    if er >= 30 and "raidwide" not in tags:
        tags.append("raidwide")

    return tags


# ============================================================================
# GEOMETRIC CLASSIFICATION
# ============================================================================

def classify_action(action):
    """Classify action based on CastType and EffectRange (geometric shape)."""
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
    all_stack_ids = set()
    all_prey_ids = set()
    all_marker_ids = set()

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
            tags = tag_action(action)
            if cls not in encounter_actions["actions"]:
                encounter_actions["actions"][cls] = []
            action_entry = {
                "id": action["id"],
                "name": action["name"],
                "cast_type": action["cast_type"],
                "effect_range": action["effect_range"],
                "cast_time_s": action["cast_time_s"],
            }
            if tags:
                action_entry["tags"] = tags
            encounter_actions["actions"][cls].append(action_entry)

            # Add to flat lists (geometric)
            if cls in ("raidwide", "aoe", "cone", "line"):
                all_area_ids.add(action["id"])
            elif cls == "single_target":
                all_tank_ids.add(action["id"])

            # Add to semantic flat lists
            for t in tags:
                if t == "stack":
                    all_stack_ids.add(action["id"])
                elif t == "prey":
                    all_prey_ids.add(action["id"])
                elif t == "marker":
                    all_marker_ids.add(action["id"])

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
    print(f"Total stack IDs found: {len(all_stack_ids)}")
    print(f"Total prey IDs found: {len(all_prey_ids)}")
    print(f"Total marker IDs found: {len(all_marker_ids)}")
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

    # Save semantic ID lists
    stack_file = os.path.join(OUTPUT_DIR, "HostileCastingStack.json")
    with open(stack_file, "w", encoding="utf-8") as f:
        json.dump(sorted(all_stack_ids), f, indent=2)
    print(f"HostileCastingStack.json: {len(all_stack_ids)} IDs")

    prey_file = os.path.join(OUTPUT_DIR, "HostileCastingPrey.json")
    with open(prey_file, "w", encoding="utf-8") as f:
        json.dump(sorted(all_prey_ids), f, indent=2)
    print(f"HostileCastingPrey.json: {len(all_prey_ids)} IDs")

    marker_file = os.path.join(OUTPUT_DIR, "HostileCastingMarker.json")
    with open(marker_file, "w", encoding="utf-8") as f:
        json.dump(sorted(all_marker_ids), f, indent=2)
    print(f"HostileCastingMarker.json: {len(all_marker_ids)} IDs")

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
