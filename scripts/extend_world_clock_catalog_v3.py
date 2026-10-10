# SPDX-License-Identifier: MIT
# VBWR B
#
# Project: WorkTrail
# Repository: https://github.com/umbertotechnopreneur/WorkTrail
# Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
#
# VibeWare: Human intent, AI execution, and plenty of tokens
# Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
#
# Modified with AI: OpenAI Codex; added this header on 2026-10-10.
# Human guidance: Umberto Giacobbi; requested VibeWare branding.
#
# Copyright (c) 2026 Umberto Giacobbi
# License: MIT - see LICENSE
#
# VBWR E

"""Extend the watercolor world-clock catalog with the September 2026 city batch."""

from __future__ import annotations

import argparse
import hashlib
import json
import zipfile
from pathlib import Path


CITY_UPDATES = json.loads(r'''[
  {
    "cityId": "ferrara",
    "displayName": "Ferrara",
    "countryCode": "IT",
    "countryName": "Italy",
    "hemisphere": "north",
    "landmarks": [
      "Castello Estense with its four towers and moat",
      "Ferrara Cathedral bell tower, restrained Renaissance terracotta rooftops and authentic Po Valley vegetation"
    ],
    "seasonalMode": "mild-winter",
    "summerPalette": "warm terracotta, sunlit brick, muted ochre, soft sage and restrained summer green",
    "summerCues": "warm Po Valley light and full restrained foliage",
    "winterPalette": "cool terracotta, damp brick, silver-blue water, muted olive and pale winter gray",
    "winterCues": "bare deciduous trees, cool mist over the moat, muted brick and no heavy snow",
    "geonameId": 3177090,
    "catalogName": "Ferrara",
    "isCapital": false
  },
  {
    "cityId": "domegge-di-cadore",
    "displayName": "Domegge di Cadore",
    "countryCode": "IT",
    "countryName": "Italy",
    "hemisphere": "north",
    "landmarks": [
      "the bell tower of San Giorgio",
      "the Centro Cadore lakefront beneath the Marmarole and Dolomite peaks"
    ],
    "seasonalMode": "true-winter",
    "summerPalette": "Dolomite limestone, lake turquoise, larch green, warm village ochre and restrained alpine meadow tones",
    "summerCues": "clear alpine summer light, green slopes and open water",
    "winterPalette": "snow white, cool Dolomite gray, muted spruce, frozen blue and warm restrained village stone",
    "winterCues": "snow on mountains and village roofs, bare larches, evergreen spruces and pale low-angle light; no active snowfall",
    "geonameId": 3177539,
    "catalogName": "Domegge di Cadore",
    "isCapital": false
  },
  {
    "cityId": "bologna",
    "displayName": "Bologna",
    "countryCode": "IT",
    "countryName": "Italy",
    "hemisphere": "north",
    "landmarks": [
      "the Asinelli and Garisenda Two Towers",
      "Basilica of San Petronio, red arcaded rooftops and authentic Emilia-Romagna vegetation"
    ],
    "seasonalMode": "mild-winter",
    "summerPalette": "Bologna red, warm terracotta, golden ochre, muted sage and restrained summer green",
    "summerCues": "warm Emilia-Romagna light and full restrained foliage",
    "winterPalette": "cool brick red, muted umber, slate blue, damp stone and pale winter gray",
    "winterCues": "bare deciduous trees, damp portico stone, cool low light and at most rare roof-edge frost",
    "geonameId": 3181928,
    "catalogName": "Bologna",
    "isCapital": false
  },
  {
    "cityId": "barcelona",
    "displayName": "Barcelona",
    "countryCode": "ES",
    "countryName": "Spain",
    "hemisphere": "north",
    "landmarks": [
      "Sagrada Família",
      "a restrained Montjuïc and Eixample roofline with Mediterranean vegetation"
    ],
    "seasonalMode": "mild-winter",
    "summerPalette": "warm limestone, muted terracotta, Mediterranean blue, olive and restrained palm green",
    "summerCues": "bright dry Mediterranean summer light and full evergreen vegetation",
    "winterPalette": "cool limestone, soft slate blue, muted umber and subdued evergreen green",
    "winterCues": "softer low winter light, damp stone and unchanged Mediterranean evergreens; no snow",
    "geonameId": 3128760,
    "catalogName": "Barcelona",
    "isCapital": false
  },
  {
    "cityId": "florence",
    "displayName": "Florence",
    "countryCode": "IT",
    "countryName": "Italy",
    "hemisphere": "north",
    "landmarks": [
      "the Cathedral of Santa Maria del Fiore and Giotto's Campanile",
      "Palazzo Vecchio above restrained terracotta roofs and Tuscan vegetation"
    ],
    "seasonalMode": "mild-winter",
    "summerPalette": "warm terracotta, pale marble, ochre, cypress green and muted Arno blue",
    "summerCues": "golden Tuscan summer light with full cypress and deciduous foliage",
    "winterPalette": "cool marble, muted terracotta, slate blue, olive and pale umber",
    "winterCues": "bare deciduous branches, evergreen cypresses retained, damp roofs and no heavy snow",
    "geonameId": 3176959,
    "catalogName": "Florence",
    "isCapital": false
  },
  {
    "cityId": "porto",
    "displayName": "Porto",
    "countryCode": "PT",
    "countryName": "Portugal",
    "hemisphere": "north",
    "landmarks": [
      "Dom Luís I Bridge",
      "Ribeira's tiled riverfront and Clérigos Tower"
    ],
    "seasonalMode": "mild-winter",
    "summerPalette": "sunlit granite, muted azulejo blue, terracotta, Douro blue and restrained green",
    "summerCues": "clear Atlantic summer light and lively but restrained riverfront color",
    "winterPalette": "cool granite, rain blue, muted tile cyan, soft umber and subdued green",
    "winterCues": "damp stone, cooler Atlantic haze and sparse deciduous foliage; no snow",
    "geonameId": 2735943,
    "catalogName": "Porto",
    "isCapital": false
  },
  {
    "cityId": "seville",
    "displayName": "Seville",
    "countryCode": "ES",
    "countryName": "Spain",
    "hemisphere": "north",
    "landmarks": [
      "the Giralda",
      "Seville Cathedral and restrained orange-tree rooftops"
    ],
    "seasonalMode": "mild-winter",
    "summerPalette": "warm sandstone, burnt sienna, muted orange, olive and pale turquoise",
    "summerCues": "dry Andalusian summer light, evergreen orange trees and heat-softened distant edges",
    "winterPalette": "cool sandstone, soft ochre, muted olive and pale blue-gray",
    "winterCues": "gentler low sun, greener vegetation and damp stone; no frost or snow",
    "geonameId": 2510911,
    "catalogName": "Sevilla",
    "isCapital": false
  },
  {
    "cityId": "lyon",
    "displayName": "Lyon",
    "countryCode": "FR",
    "countryName": "France",
    "hemisphere": "north",
    "landmarks": [
      "Basilica of Notre-Dame de Fourvière",
      "the Saône riverfront and Vieux Lyon roofs"
    ],
    "seasonalMode": "mild-winter",
    "summerPalette": "warm limestone, terracotta, river blue, muted sage and leafy green",
    "summerCues": "full riverbank foliage and clear warm continental light",
    "winterPalette": "cool limestone, slate river blue, muted umber and gray-violet",
    "winterCues": "bare riverbank trees, damp roofs and pale low sun; little or no snow",
    "geonameId": 2996944,
    "catalogName": "Lyon",
    "isCapital": false
  },
  {
    "cityId": "munich",
    "displayName": "Munich",
    "countryCode": "DE",
    "countryName": "Germany",
    "hemisphere": "north",
    "landmarks": [
      "Frauenkirche's twin domes",
      "the New Town Hall and restrained distant Alps"
    ],
    "seasonalMode": "true-winter",
    "summerPalette": "warm limestone, copper green, Bavarian blue, muted roof red and fresh foliage",
    "summerCues": "full deciduous foliage and clear continental summer light",
    "winterPalette": "snow white, slate blue, cool limestone, muted copper and pale umber",
    "winterCues": "bare trees, restrained settled snow and soft low-angle light; no active snowfall",
    "geonameId": 2867714,
    "catalogName": "Munich",
    "isCapital": false
  },
  {
    "cityId": "krakow",
    "displayName": "Kraków",
    "countryCode": "PL",
    "countryName": "Poland",
    "hemisphere": "north",
    "landmarks": [
      "Wawel Castle",
      "St. Mary's Basilica and the restrained Old Town roofline"
    ],
    "seasonalMode": "true-winter",
    "summerPalette": "warm limestone, brick red, copper green, Vistula blue and leafy green",
    "summerCues": "full riverbank foliage and luminous Central European summer light",
    "winterPalette": "snow white, slate blue, cool stone, muted brick and gray-violet",
    "winterCues": "bare trees, restrained snow on roofs and embankment, pale low light; no falling snow",
    "geonameId": 3094802,
    "catalogName": "Kraków",
    "isCapital": false
  },
  {
    "cityId": "edinburgh",
    "displayName": "Edinburgh",
    "countryCode": "GB",
    "countryName": "United Kingdom",
    "hemisphere": "north",
    "landmarks": [
      "Edinburgh Castle on Castle Rock",
      "the Scott Monument and restrained Old Town ridge"
    ],
    "seasonalMode": "mild-winter",
    "summerPalette": "warm gray stone, heather violet, muted green and pale northern blue",
    "summerCues": "fresh green slopes and clear soft northern summer light",
    "winterPalette": "cool basalt gray, slate blue, muted heather and damp umber",
    "winterCues": "bare deciduous trees, damp stone and low overcast light; at most rare frost, no heavy snow",
    "geonameId": 2650225,
    "catalogName": "Edinburgh",
    "isCapital": false
  },
  {
    "cityId": "brno",
    "displayName": "Brno",
    "countryCode": "CZ",
    "countryName": "Czechia",
    "hemisphere": "north",
    "landmarks": [
      "Špilberk Castle",
      "the Cathedral of St. Peter and Paul above restrained Moravian roofs"
    ],
    "seasonalMode": "true-winter",
    "summerPalette": "warm stone, roof red, muted gold, leafy green and pale blue",
    "summerCues": "full hillside foliage and clear Moravian summer light",
    "winterPalette": "snow white, cool stone, slate blue, muted brick and pale umber",
    "winterCues": "bare trees, restrained settled snow and low winter light; no active snowfall",
    "geonameId": 3078610,
    "catalogName": "Brno",
    "isCapital": false
  },
  {
    "cityId": "bruges",
    "displayName": "Bruges",
    "countryCode": "BE",
    "countryName": "Belgium",
    "hemisphere": "north",
    "landmarks": [
      "the Belfry of Bruges",
      "stepped-gable canal houses and restrained waterside vegetation"
    ],
    "seasonalMode": "mild-winter",
    "summerPalette": "warm brick, sandstone, canal blue, muted green and soft flower accents",
    "summerCues": "leafy canal edges and gentle northern summer light",
    "winterPalette": "cool brick, slate canal blue, damp stone, muted umber and gray-green",
    "winterCues": "bare canal trees, damp masonry and pale low light; little or no snow",
    "geonameId": 2800931,
    "catalogName": "Brugge",
    "isCapital": false
  },
  {
    "cityId": "sao-paulo",
    "displayName": "São Paulo",
    "countryCode": "BR",
    "countryName": "Brazil",
    "hemisphere": "south",
    "landmarks": [
      "the Altino Arantes tower",
      "the Copan building within a restrained dense high-rise skyline"
    ],
    "seasonalMode": "wet-dry",
    "summerPalette": "humid blue-gray, warm concrete, jacaranda green, muted terracotta and soft violet",
    "summerCues": "lush southern-summer vegetation, humid light and rain-season softness without painted rain",
    "winterPalette": "clear pale blue, cool concrete, straw green and muted umber",
    "winterCues": "drier southern-winter air, lower sun and slightly subdued vegetation; no cold-weather snow",
    "geonameId": 3448439,
    "catalogName": "São Paulo",
    "isCapital": false
  },
  {
    "cityId": "rio-de-janeiro",
    "displayName": "Rio de Janeiro",
    "countryCode": "BR",
    "countryName": "Brazil",
    "hemisphere": "south",
    "landmarks": [
      "Christ the Redeemer on Corcovado",
      "Sugarloaf Mountain and the Guanabara Bay shoreline"
    ],
    "seasonalMode": "wet-dry",
    "summerPalette": "lush tropical green, granite gray, Guanabara blue, warm sand and muted coral",
    "summerCues": "humid lush southern summer with softened distant mountains and full tropical vegetation",
    "winterPalette": "clear cobalt blue, cool granite, muted green and pale sand",
    "winterCues": "drier southern-winter clarity, crisp mountain edges and unchanged tropical vegetation",
    "geonameId": 3451190,
    "catalogName": "Rio de Janeiro",
    "isCapital": false
  },
  {
    "cityId": "salvador",
    "displayName": "Salvador",
    "countryCode": "BR",
    "countryName": "Brazil",
    "hemisphere": "south",
    "landmarks": [
      "Elevador Lacerda",
      "Pelourinho's colorful colonial roofline above All Saints Bay"
    ],
    "seasonalMode": "palette-only",
    "summerPalette": "warm ochre, muted colonial coral, bay turquoise, palm green and sunlit cream",
    "summerCues": "hot bright southern-summer light with stable tropical vegetation",
    "winterPalette": "cooler cream, rain-washed blue, muted coral and deep tropical green",
    "winterCues": "softer humid southern-winter light and rain-washed masonry; no cold cues",
    "geonameId": 3450554,
    "catalogName": "Salvador",
    "isCapital": false
  },
  {
    "cityId": "recife",
    "displayName": "Recife",
    "countryCode": "BR",
    "countryName": "Brazil",
    "hemisphere": "south",
    "landmarks": [
      "Recife Antigo's Marco Zero waterfront",
      "the Francisco Brennand sculpture column and restrained bridges over the Capibaribe"
    ],
    "seasonalMode": "palette-only",
    "summerPalette": "warm cream, muted coral, estuary blue, palm green and sunlit ochre",
    "summerCues": "bright hot southern-summer light and clear waterfront edges",
    "winterPalette": "cool blue-gray, rain-washed cream, muted terracotta and lush deep green",
    "winterCues": "humid southern-winter softness and fuller tropical vegetation; no cold cues",
    "geonameId": 3390760,
    "catalogName": "Recife",
    "isCapital": false
  },
  {
    "cityId": "fortaleza",
    "displayName": "Fortaleza",
    "countryCode": "BR",
    "countryName": "Brazil",
    "hemisphere": "south",
    "landmarks": [
      "the Beira-Mar high-rise shoreline",
      "Ponte dos Ingleses and restrained coastal palms"
    ],
    "seasonalMode": "wet-dry",
    "summerPalette": "humid turquoise, warm sand, muted concrete, palm green and soft coral",
    "summerCues": "wetter-season haze and lush coastal vegetation without painted rain",
    "winterPalette": "clear Atlantic blue, pale sand, cool concrete and restrained dry-season green",
    "winterCues": "drier southern-winter clarity, crisp shoreline and unchanged tropical palms",
    "geonameId": 3399415,
    "catalogName": "Fortaleza",
    "isCapital": false
  },
  {
    "cityId": "manaus",
    "displayName": "Manaus",
    "countryCode": "BR",
    "countryName": "Brazil",
    "hemisphere": "south",
    "landmarks": [
      "the Amazonas Theatre",
      "a restrained Rio Negro waterfront framed by authentic rainforest vegetation"
    ],
    "seasonalMode": "wet-dry",
    "summerPalette": "deep rainforest green, humid blue-gray, warm ochre, river brown and muted coral",
    "summerCues": "lush high-water-season vegetation and humid softness without painted rain",
    "winterPalette": "clearer river blue, sun-warmed ochre, muted green and dry-season gold",
    "winterCues": "lower-water dry-season clarity while preserving dense tropical vegetation; no cold cues",
    "geonameId": 3663517,
    "catalogName": "Manaus",
    "isCapital": false
  },
  {
    "cityId": "belem",
    "displayName": "Belém",
    "countryCode": "BR",
    "countryName": "Brazil",
    "hemisphere": "south",
    "landmarks": [
      "Ver-o-Peso market's iron towers",
      "the Guajará Bay waterfront and restrained tropical vegetation"
    ],
    "seasonalMode": "wet-dry",
    "summerPalette": "humid blue-gray, muted iron green, warm ochre, river brown and lush tropical green",
    "summerCues": "rain-season softness and saturated vegetation without painted rain",
    "winterPalette": "clear pale blue, sunlit cream, restrained green and warm river umber",
    "winterCues": "slightly drier-season clarity with unchanged tropical vegetation; no cold cues",
    "geonameId": 3405870,
    "catalogName": "Belém",
    "isCapital": false
  },
  {
    "cityId": "curitiba",
    "displayName": "Curitiba",
    "countryCode": "BR",
    "countryName": "Brazil",
    "hemisphere": "south",
    "landmarks": [
      "the Botanical Garden glasshouse",
      "distinctive Paraná araucaria trees and a restrained low city skyline"
    ],
    "seasonalMode": "mild-winter",
    "summerPalette": "glasshouse pale green, garden emerald, warm stone, soft blue and muted flower color",
    "summerCues": "lush southern-summer gardens and bright subtropical light",
    "winterPalette": "cool glass blue, muted green, pale stone and gray-violet",
    "winterCues": "cooler southern-winter light, sparse deciduous foliage, araucarias retained and at most light frost; no snow",
    "geonameId": 3464975,
    "catalogName": "Curitiba",
    "isCapital": false
  },
  {
    "cityId": "porto-alegre",
    "displayName": "Porto Alegre",
    "countryCode": "BR",
    "countryName": "Brazil",
    "hemisphere": "south",
    "landmarks": [
      "the Usina do Gasômetro chimney",
      "the Guaíba waterfront and restrained downtown skyline"
    ],
    "seasonalMode": "mild-winter",
    "summerPalette": "warm brick, Guaíba blue, leafy green, muted concrete and sunset-free soft ochre",
    "summerCues": "full southern-summer foliage and warm clear waterfront light",
    "winterPalette": "cool brick, slate water blue, muted umber and gray-green",
    "winterCues": "bare deciduous branches, cool damp southern-winter light and no snow",
    "geonameId": 3452925,
    "catalogName": "Porto Alegre",
    "isCapital": false
  },
  {
    "cityId": "medellin",
    "displayName": "Medellín",
    "countryCode": "CO",
    "countryName": "Colombia",
    "hemisphere": "north",
    "landmarks": [
      "the Aburrá Valley skyline",
      "Pueblito Paisa on Nutibara Hill and restrained cable-car lines"
    ],
    "seasonalMode": "palette-only",
    "summerPalette": "Andean green, warm brick, humid blue-gray, muted concrete and soft flower accents",
    "summerCues": "lush evergreen valley vegetation and bright highland light",
    "winterPalette": "cooler blue-green, muted brick, misty gray and deep evergreen",
    "winterCues": "subtle humid highland softness while preserving evergreen vegetation; no cold or snow cues",
    "geonameId": 3674962,
    "catalogName": "Medellín",
    "isCapital": false
  },
  {
    "cityId": "cali",
    "displayName": "Cali",
    "countryCode": "CO",
    "countryName": "Colombia",
    "hemisphere": "north",
    "landmarks": [
      "Cristo Rey above the city",
      "Torre de Cali and the restrained western Cordillera skyline"
    ],
    "seasonalMode": "palette-only",
    "summerPalette": "tropical green, warm concrete, pale mountain blue, muted terracotta and soft ochre",
    "summerCues": "bright warm valley light and full tropical vegetation",
    "winterPalette": "cooler blue-gray, deep green, muted concrete and soft umber",
    "winterCues": "slightly softer humid valley atmosphere with unchanged tropical vegetation; no cold cues",
    "geonameId": 3687925,
    "catalogName": "Cali",
    "isCapital": false
  },
  {
    "cityId": "cartagena",
    "displayName": "Cartagena",
    "countryCode": "CO",
    "countryName": "Colombia",
    "hemisphere": "north",
    "landmarks": [
      "the Clock Tower Gate of the walled city",
      "San Felipe de Barajas Castle and restrained Caribbean palms"
    ],
    "seasonalMode": "palette-only",
    "summerPalette": "warm colonial ochre, muted coral, Caribbean turquoise, limestone and palm green",
    "summerCues": "bright tropical light and stable coastal vegetation",
    "winterPalette": "softer turquoise, cool limestone, muted ochre and deep palm green",
    "winterCues": "subtly cooler trade-wind clarity with unchanged tropical architecture and vegetation; no cold cues",
    "geonameId": 3687238,
    "catalogName": "Cartagena",
    "isCapital": false
  },
  {
    "cityId": "cusco",
    "displayName": "Cusco",
    "countryCode": "PE",
    "countryName": "Peru",
    "hemisphere": "south",
    "landmarks": [
      "Cusco Cathedral and the Church of the Society of Jesus at Plaza de Armas",
      "Inca stone terraces beneath restrained Andean slopes"
    ],
    "seasonalMode": "wet-dry",
    "summerPalette": "rain-washed terracotta, Andean green, warm stone, cloud blue-gray and muted gold",
    "summerCues": "lush southern-summer wet-season slopes and humid high-altitude softness without painted rain",
    "winterPalette": "clear cobalt blue, dry ochre, cool stone, straw gold and muted green",
    "winterCues": "crisp dry southern-winter air, sparse highland vegetation and no city snow",
    "geonameId": 3941584,
    "catalogName": "Cusco",
    "isCapital": false
  },
  {
    "cityId": "arequipa",
    "displayName": "Arequipa",
    "countryCode": "PE",
    "countryName": "Peru",
    "hemisphere": "south",
    "landmarks": [
      "the Basilica Cathedral of Arequipa",
      "El Misti volcano above restrained white sillar roofs"
    ],
    "seasonalMode": "wet-dry",
    "summerPalette": "rain-softened sillar white, muted terracotta, Andean green, cloud blue-gray and volcanic umber",
    "summerCues": "southern-summer wet-season softness with greener foothills and no painted rain",
    "winterPalette": "crisp sillar white, cobalt blue, dry ochre, volcanic gray and straw green",
    "winterCues": "clear dry southern-winter air, sharp volcano profile and no city snow",
    "geonameId": 3947322,
    "catalogName": "Arequipa",
    "isCapital": false
  },
  {
    "cityId": "la-paz",
    "displayName": "La Paz",
    "countryCode": "BO",
    "countryName": "Bolivia",
    "hemisphere": "south",
    "landmarks": [
      "the city bowl beneath snowcapped Illimani",
      "a restrained Mi Teleférico cable-car line and San Francisco Basilica"
    ],
    "seasonalMode": "wet-dry",
    "summerPalette": "rain-washed brick, Andean green, cloud blue-gray, muted ochre and snow white on distant Illimani",
    "summerCues": "southern-summer wet-season softness and greener highland slopes without painted rain",
    "winterPalette": "clear high-altitude blue, dry brick, straw gold, cool stone and snow white on distant Illimani",
    "winterCues": "crisp dry southern-winter air, sparse slopes and strong mountain clarity; no active snowfall",
    "geonameId": 3911925,
    "catalogName": "La Paz",
    "isCapital": false
  },
  {
    "cityId": "sucre",
    "displayName": "Sucre",
    "countryCode": "BO",
    "countryName": "Bolivia",
    "hemisphere": "south",
    "landmarks": [
      "the Metropolitan Cathedral and Casa de la Libertad",
      "Sucre's white colonial roofs against restrained Andean hills"
    ],
    "seasonalMode": "wet-dry",
    "summerPalette": "rain-washed white, terracotta, Andean green, cloud blue-gray and muted flower accents",
    "summerCues": "greener southern-summer hills and humid highland softness without painted rain",
    "winterPalette": "crisp colonial white, cobalt blue, dry ochre, muted terracotta and straw green",
    "winterCues": "clear dry southern-winter air, subdued vegetation and no city snow",
    "geonameId": 3903987,
    "catalogName": "Sucre",
    "isCapital": true
  },
  {
    "cityId": "cordoba-argentina",
    "displayName": "Córdoba",
    "countryCode": "AR",
    "countryName": "Argentina",
    "hemisphere": "south",
    "landmarks": [
      "Córdoba Cathedral's twin towers and dome",
      "the Jesuit Block and restrained red-tile city roofs"
    ],
    "seasonalMode": "mild-winter",
    "summerPalette": "warm cream, terracotta, jacaranda green, muted violet and pale blue",
    "summerCues": "full southern-summer foliage and warm continental light",
    "winterPalette": "cool cream, muted terracotta, slate blue, soft umber and gray-green",
    "winterCues": "bare deciduous trees, clear low southern-winter sun and no snow",
    "geonameId": 3860259,
    "catalogName": "Córdoba",
    "isCapital": false
  },
  {
    "cityId": "rosario",
    "displayName": "Rosario",
    "countryCode": "AR",
    "countryName": "Argentina",
    "hemisphere": "south",
    "landmarks": [
      "the National Flag Memorial",
      "the Paraná riverfront and restrained central skyline"
    ],
    "seasonalMode": "mild-winter",
    "summerPalette": "warm limestone, Paraná blue, leafy green, muted concrete and soft ochre",
    "summerCues": "lush southern-summer riverfront foliage and humid warm light",
    "winterPalette": "cool limestone, slate river blue, muted umber and pale gray-green",
    "winterCues": "bare deciduous trees, clearer low southern-winter light and no snow",
    "geonameId": 3838583,
    "catalogName": "Rosario",
    "isCapital": false
  },
  {
    "cityId": "mendoza",
    "displayName": "Mendoza",
    "countryCode": "AR",
    "countryName": "Argentina",
    "hemisphere": "south",
    "landmarks": [
      "the Army of the Andes monument on Cerro de la Gloria",
      "a restrained tree-lined low city beneath the Andes"
    ],
    "seasonalMode": "mild-winter",
    "summerPalette": "dry ochre, leafy irrigation green, warm stone, high-altitude blue and distant mountain gray",
    "summerCues": "full southern-summer plane-tree foliage, dry foothills and bright highland light",
    "winterPalette": "cool ochre, bare branch umber, slate blue, muted green and snow white on distant Andes",
    "winterCues": "bare city trees, crisp dry southern-winter air and snow only on the distant Andes",
    "geonameId": 3844421,
    "catalogName": "Mendoza",
    "isCapital": false
  },
  {
    "cityId": "valparaiso",
    "displayName": "Valparaíso",
    "countryCode": "CL",
    "countryName": "Chile",
    "hemisphere": "south",
    "landmarks": [
      "the colorful houses and historic ascensores of Cerro Alegre",
      "the restrained port and bay amphitheater"
    ],
    "seasonalMode": "mild-winter",
    "summerPalette": "muted coastal coral, ochre, Pacific blue, dry hillside green and warm cream",
    "summerCues": "clear dry southern-summer coastal light and restrained colorful hillsides",
    "winterPalette": "cool Pacific blue, rain-washed coral, muted ochre, gray-green and damp cream",
    "winterCues": "softer wetter southern-winter atmosphere, greener slopes and no snow",
    "geonameId": 3868626,
    "catalogName": "Valparaíso",
    "isCapital": false
  },
  {
    "cityId": "surabaya",
    "displayName": "Surabaya",
    "countryCode": "ID",
    "countryName": "Indonesia",
    "hemisphere": "equatorial",
    "landmarks": [
      "Tugu Pahlawan and its flame-shaped crown",
      "a restrained Surabaya skyline, tropical trees and low civic buildings"
    ],
    "seasonalMode": "wet-dry",
    "summerPalette": "pale tropical blue, warm stone, lush green, muted teal and soft ochre",
    "summerCues": "lush wet-season tropical foliage and warm humid daylight without painted rain",
    "winterPalette": "sun-warmed stone, subdued olive, muted teal, dry-season gold and pale blue",
    "winterCues": "slightly drier tropical foliage and clear warm daylight; no cold cues or snow",
    "geonameId": 1625822,
    "catalogName": "Surabaya",
    "isCapital": false
  },
  {
    "cityId": "bekasi",
    "displayName": "Bekasi",
    "countryCode": "ID",
    "countryName": "Indonesia",
    "hemisphere": "equatorial",
    "landmarks": [
      "Masjid Agung Al-Barkah with its central dome and minarets",
      "a restrained Bekasi mid-rise skyline and tropical streetscape"
    ],
    "seasonalMode": "wet-dry",
    "summerPalette": "pale blue, tropical leaf green, warm cream, muted teal and restrained gold",
    "summerCues": "lush wet-season greenery and warm humid light without painted rain",
    "winterPalette": "soft blue-gray, warm cream, subdued olive, pale terracotta and dry-season gold",
    "winterCues": "slightly drier tropical foliage and gentle warm daylight; no cold cues or snow",
    "geonameId": 1649378,
    "catalogName": "Bekasi",
    "isCapital": false
  },
  {
    "cityId": "bandung",
    "displayName": "Bandung",
    "countryCode": "ID",
    "countryName": "Indonesia",
    "hemisphere": "equatorial",
    "landmarks": [
      "Gedung Sate and its distinctive central roof finial",
      "Bandung's restrained highland skyline, tropical gardens and low-rise roofs"
    ],
    "seasonalMode": "wet-dry",
    "summerPalette": "pale highland blue, warm cream, lush green, muted terracotta and soft ochre",
    "summerCues": "lush rainy-season gardens and soft warm highland daylight without painted rain",
    "winterPalette": "cool highland blue, pale stone, subdued olive, muted terracotta and dry-season gold",
    "winterCues": "slightly drier highland foliage and clear mild daylight; no snow or cold effects",
    "geonameId": 1650357,
    "catalogName": "Bandung",
    "isCapital": false
  },
  {
    "cityId": "medan",
    "displayName": "Medan",
    "countryCode": "ID",
    "countryName": "Indonesia",
    "hemisphere": "equatorial",
    "landmarks": [
      "the yellow Maimun Palace with its Malay-Islamic arches",
      "the Great Mosque of Medan and a restrained tropical city skyline"
    ],
    "seasonalMode": "wet-dry",
    "summerPalette": "warm palace yellow, pale tropical blue, lush green, cream and muted teal",
    "summerCues": "full wet-season tropical foliage and warm humid light without painted rain",
    "winterPalette": "soft ochre, warm cream, muted teal, subdued olive and pale blue-gray",
    "winterCues": "slightly drier tropical foliage and clear warm daylight; no cold cues or snow",
    "geonameId": 1214520,
    "catalogName": "Medan",
    "isCapital": false
  },
  {
    "cityId": "depok",
    "displayName": "Depok",
    "countryCode": "ID",
    "countryName": "Indonesia",
    "hemisphere": "equatorial",
    "landmarks": [
      "Masjid Dian Al-Mahri's gold-clad domes and slender minarets",
      "Depok's restrained mid-rise skyline and tropical tree canopy"
    ],
    "seasonalMode": "wet-dry",
    "summerPalette": "soft gold, pale tropical blue, lush green, warm cream and muted teal",
    "summerCues": "lush wet-season greenery and humid tropical light without painted rain",
    "winterPalette": "muted gold, warm cream, subdued olive, pale blue-gray and dry-season ochre",
    "winterCues": "slightly drier tropical foliage and gentle warm daylight; no cold cues or snow",
    "geonameId": 1645524,
    "catalogName": "Depok",
    "isCapital": false
  },
  {
    "cityId": "tangerang",
    "displayName": "Tangerang",
    "countryCode": "ID",
    "countryName": "Indonesia",
    "hemisphere": "equatorial",
    "landmarks": [
      "Al-Azhom Grand Mosque's broad blue dome and four minarets",
      "a restrained Tangerang skyline with tropical trees and low civic buildings"
    ],
    "seasonalMode": "wet-dry",
    "summerPalette": "pale blue, tropical green, warm cream, soft ochre and muted teal",
    "summerCues": "lush wet-season tropical foliage and warm humid daylight without painted rain",
    "winterPalette": "cool pale blue, warm stone, subdued olive, muted teal and dry-season gold",
    "winterCues": "slightly drier foliage and clear warm daylight; no cold cues or snow",
    "geonameId": 1625084,
    "catalogName": "Tangerang",
    "isCapital": false
  },
  {
    "cityId": "palembang",
    "displayName": "Palembang",
    "countryCode": "ID",
    "countryName": "Indonesia",
    "hemisphere": "equatorial",
    "landmarks": [
      "Ampera Bridge's distinctive twin towers and long span over the Musi River",
      "Palembang's restrained riverside skyline and tropical vegetation"
    ],
    "seasonalMode": "wet-dry",
    "summerPalette": "muted river blue, pale tropical blue, lush green, warm cream and soft vermilion",
    "summerCues": "lush wet-season riverbank foliage and warm humid light without painted rain",
    "winterPalette": "soft slate blue, warm cream, subdued olive, muted vermilion and dry-season ochre",
    "winterCues": "slightly drier riverbank foliage and clear warm daylight; no cold cues or snow",
    "geonameId": 1633070,
    "catalogName": "Palembang",
    "isCapital": false
  },
  {
    "cityId": "semarang",
    "displayName": "Semarang",
    "countryCode": "ID",
    "countryName": "Indonesia",
    "hemisphere": "equatorial",
    "landmarks": [
      "Lawang Sewu's twin corner towers and arcaded historic facade",
      "Semarang's restrained urban skyline, tropical trees and low-rise roofs"
    ],
    "seasonalMode": "wet-dry",
    "summerPalette": "warm ivory, muted terracotta, pale tropical blue, lush green and soft ochre",
    "summerCues": "lush wet-season gardens and humid tropical daylight without painted rain",
    "winterPalette": "cool ivory, subdued terracotta, slate blue, muted olive and dry-season gold",
    "winterCues": "slightly drier foliage and clear warm daylight; no cold cues or snow",
    "geonameId": 1627896,
    "catalogName": "Semarang",
    "isCapital": false
  },
  {
    "cityId": "denpasar",
    "displayName": "Denpasar",
    "countryCode": "ID",
    "countryName": "Indonesia",
    "hemisphere": "equatorial",
    "landmarks": [
      "Bajra Sandhi Monument's stepped form and central tower",
      "Balinese split-gate stonework, tropical palms and a restrained Denpasar skyline"
    ],
    "seasonalMode": "wet-dry",
    "summerPalette": "warm temple stone, pale tropical blue, lush green, muted ochre and soft teal",
    "summerCues": "lush wet-season tropical gardens and warm humid daylight without painted rain",
    "winterPalette": "warm temple stone, subdued olive, muted teal, dry-season ochre and pale blue",
    "winterCues": "slightly drier tropical gardens and clear warm daylight; no cold cues or snow",
    "geonameId": 1645528,
    "catalogName": "Denpasar",
    "isCapital": false
  },
  {
    "cityId": "haiphong",
    "displayName": "Haiphong",
    "countryCode": "VN",
    "countryName": "Vietnam",
    "hemisphere": "north",
    "landmarks": [
      "Haiphong Opera House's French neoclassical facade and domed roof",
      "a restrained northern Vietnamese skyline and flame-flower trees"
    ],
    "seasonalMode": "mild-winter",
    "summerPalette": "warm ivory, muted coral, pale river blue, leafy green and soft ochre",
    "summerCues": "full subtropical foliage and warm humid daylight without painted rain",
    "winterPalette": "cool ivory, slate blue, muted coral, damp stone and subdued gray-green",
    "winterCues": "mild cool-season light, damp stone and sparse deciduous foliage; no snow",
    "geonameId": 1581298,
    "catalogName": "Haiphong",
    "isCapital": false
  },
  {
    "cityId": "can-tho",
    "displayName": "Cần Thơ",
    "countryCode": "VN",
    "countryName": "Vietnam",
    "hemisphere": "equatorial",
    "landmarks": [
      "Can Tho Bridge spanning the Hau River",
      "a restrained Mekong Delta riverfront with tropical trees and low urban buildings"
    ],
    "seasonalMode": "wet-dry",
    "summerPalette": "soft river blue, pale tropical blue, lush green, warm cream and muted teal",
    "summerCues": "lush wet-season riverbank foliage and humid tropical light without painted rain",
    "winterPalette": "muted river blue, warm cream, subdued olive, pale blue-gray and dry-season ochre",
    "winterCues": "drier-season riverbank foliage and warm clear daylight; no cold cues or snow",
    "geonameId": 1586203,
    "catalogName": "Cần Thơ",
    "isCapital": false
  },
  {
    "cityId": "hue",
    "displayName": "Huế",
    "countryCode": "VN",
    "countryName": "Vietnam",
    "hemisphere": "north",
    "landmarks": [
      "Ngo Mon Gate of the Imperial Citadel with its layered red-tiled roof",
      "Thien Mu Pagoda above a restrained Perfume River riverfront"
    ],
    "seasonalMode": "mild-winter",
    "summerPalette": "warm imperial ochre, muted brick red, river blue, leafy green and pale cream",
    "summerCues": "full riverbank foliage and warm humid daylight without painted rain",
    "winterPalette": "cool imperial ochre, slate river blue, muted brick, damp stone and gray-green",
    "winterCues": "mild cooler-season light, damp stone and sparse deciduous foliage; no snow",
    "geonameId": 1580240,
    "catalogName": "Huế",
    "isCapital": false
  },
  {
    "cityId": "da-nang",
    "displayName": "Da Nang",
    "countryCode": "VN",
    "countryName": "Vietnam",
    "hemisphere": "north",
    "landmarks": [
      "the Dragon Bridge crossing the Han River with its unmistakable long dragon form",
      "Da Nang's restrained riverside skyline and distant Marble Mountains"
    ],
    "seasonalMode": "mild-winter",
    "summerPalette": "soft river blue, pale coastal blue, muted coral, lush green and warm cream",
    "summerCues": "full tropical riverbank foliage and warm coastal light without painted weather",
    "winterPalette": "slate river blue, cool cream, subdued coral, muted olive and damp stone gray",
    "winterCues": "mild cooler-season coastal light, damp stone and slightly sparser foliage; no snow",
    "geonameId": 1583992,
    "catalogName": "Da Nang",
    "isCapital": false
  },
  {
    "cityId": "bien-hoa",
    "displayName": "Biên Hòa",
    "countryCode": "VN",
    "countryName": "Vietnam",
    "hemisphere": "equatorial",
    "landmarks": [
      "a low bridge and riverfront along the Đồng Nai River",
      "Biên Hòa's restrained mid-rise skyline and tropical riverbank vegetation"
    ],
    "seasonalMode": "wet-dry",
    "summerPalette": "muted river blue, pale tropical blue, lush green, warm cream and soft ochre",
    "summerCues": "lush wet-season riverside foliage and humid tropical light without painted rain",
    "winterPalette": "soft slate blue, warm cream, subdued olive, pale terracotta and dry-season gold",
    "winterCues": "drier-season riverbank foliage and gentle warm daylight; no cold cues or snow",
    "geonameId": 1587923,
    "catalogName": "Biên Hòa",
    "isCapital": false
  },
  {
    "cityId": "thanh-hoa",
    "displayName": "Thanh Hóa",
    "countryCode": "VN",
    "countryName": "Vietnam",
    "hemisphere": "north",
    "landmarks": [
      "Ham Rong Bridge crossing the Ma River",
      "Thanh Hóa's restrained northern Vietnamese city skyline and riverbank trees"
    ],
    "seasonalMode": "mild-winter",
    "summerPalette": "soft river blue, warm cream, muted terracotta, lush green and pale ochre",
    "summerCues": "full subtropical foliage and warm humid daylight without painted rain",
    "winterPalette": "slate river blue, cool cream, muted terracotta, damp stone and gray-green",
    "winterCues": "mild cool-season light, damp stone and sparse deciduous foliage; no snow",
    "geonameId": 1566166,
    "catalogName": "Thanh Hóa",
    "isCapital": false
  },
  {
    "cityId": "vinh",
    "displayName": "Vinh",
    "countryCode": "VN",
    "countryName": "Vietnam",
    "hemisphere": "north",
    "landmarks": [
      "the surviving gate and low walls of Vinh Citadel",
      "a restrained urban skyline with the Lam River plain and distant Quyet Mountain"
    ],
    "seasonalMode": "mild-winter",
    "summerPalette": "warm cream, muted brick, pale blue, leafy green and soft ochre",
    "summerCues": "full subtropical greenery and warm humid daylight without painted rain",
    "winterPalette": "cool cream, slate blue, muted brick, damp stone and subdued gray-green",
    "winterCues": "mild cool-season air, softer light and sparse deciduous foliage; no snow",
    "geonameId": 1562798,
    "catalogName": "Vinh",
    "isCapital": false
  },
  {
    "cityId": "thuan-an",
    "displayName": "Thuận An",
    "countryCode": "VN",
    "countryName": "Vietnam",
    "hemisphere": "equatorial",
    "landmarks": [
      "a restrained contemporary Thuận An mid-rise skyline beside a Saigon River tributary",
      "tropical riverbank trees and low civic buildings"
    ],
    "seasonalMode": "wet-dry",
    "summerPalette": "pale tropical blue, muted river blue, lush green, warm cream and soft ochre",
    "summerCues": "lush wet-season riverbank foliage and warm humid daylight without painted rain",
    "winterPalette": "soft blue-gray, muted river blue, warm cream, subdued olive and dry-season gold",
    "winterCues": "drier-season tropical foliage and clear warm daylight; no cold cues or snow",
    "geonameId": 12382296,
    "catalogName": "Thuận An",
    "isCapital": false
  }
]''')


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_geonames(cities_zip: Path) -> dict[int, dict[str, object]]:
    rows: dict[int, dict[str, object]] = {}
    with zipfile.ZipFile(cities_zip) as archive:
        with archive.open("cities500.txt") as source:
            for raw_line in source:
                fields = raw_line.decode("utf-8").rstrip("\n").split("\t")
                if len(fields) != 19:
                    raise ValueError("Unexpected GeoNames cities500 row shape.")
                geoname_id = int(fields[0])
                rows[geoname_id] = {
                    "name": fields[1],
                    "countryCode": fields[8],
                    "latitude": float(fields[4]),
                    "longitude": float(fields[5]),
                    "population": int(fields[14]),
                    "timeZoneId": fields[17],
                }
    return rows


def update(manifest_path: Path, cities_zip: Path, master_root: Path, *, dry_run: bool) -> None:
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    cities = manifest["cities"]
    additional = manifest["additionalCatalogCities"]
    reviewed = manifest["reviewedMasters"]

    update_ids = {item["cityId"] for item in CITY_UPDATES}
    if len(update_ids) != len(CITY_UPDATES):
        raise ValueError("Catalog expansion contains duplicate city ids.")

    existing_ids = {item["cityId"] for item in cities}
    existing_additional_ids = {item["cityId"] for item in additional}
    existing_reviewed_files = {item["fileName"] for item in reviewed}
    already_present_ids = update_ids & (existing_ids | existing_additional_ids)
    for city_id in already_present_ids:
        if city_id not in existing_ids or city_id not in existing_additional_ids:
            raise ValueError(f"Catalog entry is only partially present: {city_id}")
        missing_seasons = [
            season for season in ("summer", "winter")
            if f"{city_id}-{season}.png" not in existing_reviewed_files
        ]
        if missing_seasons:
            raise ValueError(
                f"Catalog entry is missing reviewed masters: {city_id} ({', '.join(missing_seasons)})"
            )
    pending_updates = [
        item for item in CITY_UPDATES if item["cityId"] not in already_present_ids
    ]
    if len(reviewed) != len(cities) * 2:
        raise ValueError("Existing reviewed-master count is not aligned with the city catalog.")

    geonames = read_geonames(cities_zip)
    additions: list[dict[str, object]] = []
    prompt_records: list[dict[str, object]] = []
    master_records: list[dict[str, str]] = []

    for item in pending_updates:
        city_id = item["cityId"]
        geoname_id = item["geonameId"]
        row = geonames.get(geoname_id)
        if row is None:
            raise ValueError(f"GeoNames cities500 lacks {city_id} ({geoname_id}).")
        if row["name"] != item["catalogName"]:
            raise ValueError(
                f"GeoNames name mismatch for {city_id}: expected {item['catalogName']!r}, got {row['name']!r}."
            )
        if row["countryCode"] != item["countryCode"]:
            raise ValueError(
                f"GeoNames country mismatch for {city_id}: expected {item['countryCode']}, got {row['countryCode']}."
            )
        if not row["timeZoneId"]:
            raise ValueError(f"GeoNames timezone is missing for {city_id}.")

        additions.append({
            "cityId": city_id,
            "geonameId": geoname_id,
            "name": row["name"],
            "countryCode": row["countryCode"],
            "countryName": item["countryName"],
            "latitude": row["latitude"],
            "longitude": row["longitude"],
            "population": row["population"],
            "timeZoneId": row["timeZoneId"],
            "isCapital": item["isCapital"],
            "hemisphere": item["hemisphere"],
        })
        prompt_records.append({
            "cityId": city_id,
            "displayName": item["displayName"],
            "landmarks": item["landmarks"],
            "seasonalMode": item["seasonalMode"],
            "summerPalette": item["summerPalette"],
            "summerCues": item["summerCues"],
            "winterPalette": item["winterPalette"],
            "winterCues": item["winterCues"],
            "uncertain": False,
        })

        for season in ("summer", "winter"):
            file_name = f"{city_id}-{season}.png"
            if file_name in existing_reviewed_files:
                raise ValueError(f"Reviewed master already exists in manifest: {file_name}")
            master = master_root / file_name
            if not master.is_file():
                raise FileNotFoundError(f"Missing reviewed master: {master}")
            master_records.append({
                "cityId": city_id,
                "season": season,
                "fileName": file_name,
                "sha256": sha256(master),
            })

    if dry_run:
        print(json.dumps({
            "cityCount": len(additions),
            "assetCount": len(master_records),
            "alreadyPresentCount": len(already_present_ids),
            "valid": True,
        }, indent=2))
        return

    cities.extend(prompt_records)
    additional.extend(additions)
    reviewed.extend(master_records)
    manifest["assetCountExpected"] = len(cities) * 2
    if len(reviewed) != manifest["assetCountExpected"]:
        raise ValueError("Reviewed-master count is not aligned with the two-season city catalog.")

    manifest_path.write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    print(json.dumps({
        "cityCount": len(cities),
        "assetCount": manifest["assetCountExpected"],
        "updated": str(manifest_path),
    }, indent=2))


def main() -> None:
    repository_root = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--manifest",
        type=Path,
        default=repository_root / "design/world-clocks/watercolor/generation-manifest-v1.json",
    )
    parser.add_argument("--cities-zip", type=Path, required=True)
    parser.add_argument(
        "--masters",
        type=Path,
        default=repository_root / "design/world-clocks/watercolor/masters-v1",
    )
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()
    update(
        args.manifest.resolve(),
        args.cities_zip.resolve(),
        args.masters.resolve(),
        dry_run=args.dry_run,
    )


if __name__ == "__main__":
    main()
