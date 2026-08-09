# 🌍 Tileset System Guide

**Build seamless urban environments with procedural variation!**

---

## 🎯 **What Are Tilesets?**

Tilesets are **standardized, seamless voxel tiles** that snap together perfectly to build large environments.

### **Key Features:**
- ✅ **Consistent size** (128×2×128 voxels = 16m×0.25m×16m)
- ✅ **Seamless edges** (grass/sidewalks align perfectly)
- ✅ **Procedural variation** (each tile is unique)
- ✅ **Re-Generate button** (iterate until you like it!)

---

## 🏘️ **Urban Residential Tileset**

### **Available Tiles:**

| Tile | Description | Use Case |
|------|-------------|----------|
| **Grass Plane** | Full grass field | Parks, yards, open spaces |
| **Road: Straight** | Straight road with sidewalks | Main streets |
| **Road: Turn Left** | 90° left curve | Street corners |
| **Road: Turn Right** | 90° right curve | Street corners |
| **Road: 4-Way Intersection** | Four-way crossroads | Major intersections |
| **Road: T-Junction** | Three-way junction | Side streets |
| **Road: Dead End** | Cul-de-sac | Residential dead ends |

---

## 🛠️ **How to Use**

### **Step 1: Generate a Tile**

1. **Open Voxel Studio**
2. Menu → **Generate** → **🌍 Tilesets** → **Urban Residential (16m tiles)**
3. Click on any tile type (e.g., "Road: Straight")
4. **Tile generates instantly** with random variation!

### **Step 2: Iterate with Re-Generate**

1. **Look at the generated tile**
2. Don't like it? Click **🔄 Re-Generate** button (toolbar)
3. **New variation appears instantly!**
4. Keep clicking until you get one you like!

### **Step 3: Save the Tile**

1. File → **Save As**
2. Name it descriptively:
   - `Road_Straight_01.stasset`
   - `Road_Straight_02.stasset`
   - `Road_Turn_Left_01.stasset`
   - etc.
3. Save to `StreamingAssets/Tiles/` folder

### **Step 4: Build Your Neighborhood**

1. Generate multiple variations of each tile type
2. Save each one with a unique name
3. Import all tiles into Unity
4. Arrange them on a 16m grid!

---

## 🎨 **Tile Anatomy**

### **Grass Plane:**
```
Top View:
┌────────────────┐
│   GRASS        │
│   (green)      │
│                │
│   DIRT base    │
└────────────────┘
```

### **Road: Straight:**
```
Top View:
┌────────────────┐
│ GRASS          │
│ SIDEWALK       │
│ ROAD (stone)   │
│ SIDEWALK       │
│ GRASS          │
└────────────────┘
```

### **Road: 4-Way Intersection:**
```
Top View:
    GRASS
    SIDEWALK
┌───ROAD───┐
│          │
GRASS ROAD GRASS
│          │
└───ROAD───┘
    SIDEWALK
    GRASS
```

---

## 📐 **Seamless Snapping**

All tiles are **16m × 16m** (128 voxels):

```
Unity Grid Layout:
  0    16   32   48   64
0 [G ] [RS] [RL] [G ]
16[RS] [4W] [RS] [TJ]
32[G ] [RS] [DE] [G ]
48[G ] [G ] [G ] [G ]

G  = Grass Plane
RS = Road Straight
RL = Road Turn Left
4W = 4-Way Intersection
TJ = T-Junction
DE = Dead End
```

**Placement in Unity:**
- Position tiles at multiples of 16m
- Examples: (0,0,0), (16,0,0), (32,0,0), (0,0,16), etc.
- All tiles at Y=0 (ground level)

---

## 🔄 **Re-Generate Workflow**

### **Typical Usage:**

1. **Generate "Road: Straight"**
   - First variation appears
   
2. **Click 🔄 Re-Generate** (3-5 times)
   - Each click = new random variation
   - Different crack patterns
   - Different grass variations
   
3. **Found one you like?**
   - Save it!
   
4. **Need more straight roads?**
   - Generate → Road: Straight again
   - Re-Generate a few times
   - Save another variation
   
5. **Build a set:**
   - 3-5 variations of each tile type
   - Total: ~20-30 unique tiles
   - Enough variety for a whole neighborhood!

---

## 🎯 **Building a Neighborhood (Example)**

### **Tile Set (Minimum):**
```
Grass Planes: 3 variations
Road Straight: 5 variations
Road Turn Left: 3 variations
Road Turn Right: 3 variations
Road 4-Way: 2 variations
Road T-Junction: 2 variations
Road Dead End: 2 variations

Total: 20 tiles
```

### **Layout Example (4×4 blocks):**
```
[Grass] [Road] [Grass] [Grass]
[Road ] [4Way] [Road ] [TJunc]
[Grass] [Road] [Grass] [Grass]
[Grass] [Dead] [Grass] [Grass]
```

**Add buildings on grass tiles!**

---

## 💡 **Pro Tips**

### **Variation Strategy:**
- Generate 3-5 variations of common tiles (straight roads)
- Generate 2-3 variations of special tiles (intersections)
- Use different variations to avoid repetition

### **Naming Convention:**
```
Road_Straight_01.stasset
Road_Straight_02.stasset
Road_Straight_03.stasset
Road_TurnLeft_01.stasset
Road_TurnLeft_02.stasset
Grass_Plane_01.stasset
Grass_Plane_02.stasset
```

### **Organization:**
```
StreamingAssets/
├── Tiles/
│   ├── Grass/
│   │   ├── Grass_Plane_01.stasset
│   │   ├── Grass_Plane_02.stasset
│   │   └── Grass_Plane_03.stasset
│   ├── Roads/
│   │   ├── Road_Straight_01.stasset
│   │   ├── Road_Straight_02.stasset
│   │   ├── Road_TurnLeft_01.stasset
│   │   └── ...
│   └── Intersections/
│       ├── Road_4Way_01.stasset
│       ├── Road_TJunc_01.stasset
│       └── ...
```

---

## 🚀 **Quick Workflow**

**Goal: Generate a complete urban tileset (20 tiles)**

**Time: ~15 minutes**

1. **Grass Planes** (3 variations) - 2 min
   - Generate → Grass Plane
   - Re-Generate 2-3 times, save
   - Repeat 3 times

2. **Straight Roads** (5 variations) - 5 min
   - Generate → Road: Straight
   - Re-Generate 2-3 times, save
   - Repeat 5 times

3. **Turns** (6 variations) - 4 min
   - Generate → Road: Turn Left (3 variations)
   - Generate → Road: Turn Right (3 variations)

4. **Intersections** (4 variations) - 3 min
   - Generate → Road: 4-Way (2 variations)
   - Generate → Road: T-Junction (2 variations)

5. **Dead Ends** (2 variations) - 1 min
   - Generate → Road: Dead End (2 variations)

**Total: 20 unique tiles in 15 minutes!** 🎉

---

## ✅ **Benefits**

- ✅ **Fast iteration** - Re-Generate button = instant variations
- ✅ **No repetition** - Each tile is procedurally unique
- ✅ **Seamless** - All tiles snap together perfectly
- ✅ **Scalable** - Build neighborhoods of any size
- ✅ **Consistent** - All tiles same size and height

---

**Ready to build your city?** 🏘️✨
