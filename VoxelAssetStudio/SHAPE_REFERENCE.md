# Voxel Asset Studio - Shape Reference Guide

**Last Updated:** June 27, 2026

---

## 📐 **Understanding Voxel Shapes**

All shapes are built from **individual voxels** (3D pixels). Each voxel has a **material ID**:
- **0 = Air** (empty space, transparent)
- **3 = Concrete** (gray, medium strength)
- **5 = Steel** (metallic, high strength)

---

## 🎨 **Available Shapes**

### **1. Solid Shapes (Densely Packed)**

#### **Cube**
```python
generate_cube(size=(32, 32, 32), material_id=3)
```
**What it does:**
- Fills entire volume with one material
- No empty space inside
- Completely solid

**Use cases:**
- Simple test objects
- Building blocks
- Solid barriers

**Visual (cross-section):**
```
[C][C][C][C][C]
[C][C][C][C][C]
[C][C][C][C][C]
[C][C][C][C][C]
[C][C][C][C][C]
```
(C = Concrete, all voxels filled)

---

#### **Sphere**
```python
generate_sphere(grid_size=(32,32,32), radius=15, center=(16,16,16), material_id=3)
```
**What it does:**
- Creates spherical shape
- Fills all voxels within radius
- Completely solid

**Use cases:**
- Round objects
- Projectiles
- Decorative elements

**Visual (cross-section):**
```
    [C]
  [C][C][C]
[C][C][C][C][C]
  [C][C][C]
    [C]
```
(All voxels inside sphere are filled)

---

### **2. Armored Shapes (Two-Layer, Densely Packed)**

#### **Armored Cube**
```python
generate_armored_cube(size=(32,32,32), shell_thickness=2)
```
**What it does:**
- **Outer shell:** 2 voxels of Steel (material 5)
- **Inner core:** Concrete (material 3)
- **NO empty space** - completely filled

**Damage behavior:**
- Shoot outer shell → Steel turns orange (damaged)
- Shoot again → Shell destroyed, reveals concrete core
- Shoot concrete → Concrete turns crimson (damaged)
- Shoot again → Hole appears (air)

**Visual (cross-section):**
```
[S][S][C][C][C][C][S][S]
[S][S][C][C][C][C][S][S]
[S][S][C][C][C][C][S][S]
[S][S][C][C][C][C][S][S]
```
(S = Steel shell, C = Concrete core)

**Use cases:**
- Armored buildings
- Fortified walls
- Destructible cover

---

#### **Armored Sphere**
```python
generate_armored_sphere(grid_size=(32,32,32), radius=15, shell_thickness=2)
```
**What it does:**
- **Outer shell:** 2 voxels of Steel
- **Inner core:** Concrete
- **NO empty space** - completely filled

**Visual (cross-section):**
```
     [S]
   [S][C][S]
  [S][C][C][C][S]
[S][C][C][C][C][C][S]
  [S][C][C][C][S]
   [S][C][S]
     [S]
```
(S = Steel shell, C = Concrete core)

**Use cases:**
- Armored vehicles
- Enemy units
- Destructible spheres

---

#### **Armored Cylinder**
```python
generate_armored_cylinder(grid_size=(32,32,32), radius=12, height=28, shell_thickness=2)
```
**What it does:**
- **Outer shell:** 2 voxels of Steel (radial)
- **Inner core:** Concrete
- **NO empty space** - completely filled

**Visual (top-down cross-section):**
```
   [S][S][S]
 [S][C][C][C][S]
[S][C][C][C][C][C][S]
 [S][C][C][C][S]
   [S][S][S]
```
(S = Steel shell, C = Concrete core, extends vertically)

**Use cases:**
- Pillars
- Towers
- Turrets
- Vertical structures

---

### **3. Two-Layer Shell (MISLEADING NAME!)**

#### **Hollow Shell** ⚠️ **NOT ACTUALLY HOLLOW!**
```python
generate_hollow_shell(grid_size, outer_radius, inner_radius, outer_material=5, inner_material=3)
```
**What it ACTUALLY does:**
- **Outer layer:** Steel (between outer_radius and inner_radius)
- **Inner layer:** Concrete (fills everything inside inner_radius)
- **NOT HOLLOW** - has concrete core!

**This is basically the same as Armored Sphere!**

**Visual (cross-section):**
```
     [S]
   [S][C][S]
  [S][C][C][C][S]
   [S][C][S]
     [S]
```
(S = Steel outer, C = Concrete inner - NOT EMPTY!)

---

### **4. TRULY Hollow Shapes (NEW!)** ✨

#### **Truly Hollow Sphere**
```python
generate_truly_hollow_sphere(grid_size=(32,32,32), outer_radius=15, shell_thickness=2, material=5)
```
**What it does:**
- **Shell only:** 2 voxels of Steel
- **Interior:** EMPTY (air, material 0)
- **Truly hollow!**

**Visual (cross-section):**
```
     [S]
   [S][ ][S]
  [S][ ][ ][ ][S]
   [S][ ][S]
     [S]
```
(S = Steel, [ ] = Air/empty)

**Use cases:**
- Lightweight structures
- Shells that can be entered
- Fragile containers
- Decorative spheres

**Gameplay difference:**
- Shoot through shell → Bullet passes through empty interior!
- Less material to destroy
- Can create "windows" by shooting holes

---

#### **Truly Hollow Cube**
```python
generate_truly_hollow_cube(size=(32,32,32), shell_thickness=2, material=5)
```
**What it does:**
- **Shell only:** 2 voxels of Steel on all 6 faces
- **Interior:** EMPTY (air)
- **Truly hollow!**

**Visual (cross-section):**
```
[S][S][ ][ ][ ][ ][S][S]
[S][S][ ][ ][ ][ ][S][S]
[ ][ ][ ][ ][ ][ ][ ][ ]
[ ][ ][ ][ ][ ][ ][ ][ ]
[S][S][ ][ ][ ][ ][S][S]
[S][S][ ][ ][ ][ ][S][S]
```
(S = Steel shell, [ ] = Air/empty interior)

**Use cases:**
- Room interiors
- Containers
- Hollow buildings
- Lightweight armor

**Gameplay difference:**
- Shoot through wall → Bullet enters hollow interior
- Can create entry points
- Much less material to destroy

---

## 🎮 **Gameplay Implications**

### **Solid vs Armored vs Hollow:**

| Type | Material Count | Destruction Time | Bullet Behavior |
|------|---------------|------------------|-----------------|
| **Solid Cube** | 32,768 voxels | Very long | Stops at surface |
| **Armored Cube** | 32,768 voxels | Long (2-stage) | Penetrates shell → core |
| **Hollow Cube** | ~7,000 voxels | Short | Penetrates shell → passes through |

### **Strategic Differences:**

**Armored (Dense):**
- ✅ Maximum protection
- ✅ Two-stage destruction (visual feedback)
- ❌ Takes many shots to destroy
- ❌ Bullets always stop

**Truly Hollow:**
- ✅ Quick to destroy
- ✅ Bullets can pass through
- ✅ Can create entry points
- ❌ Minimal protection

---

## 🔧 **How to Choose**

### **Use Armored Shapes When:**
- You want durable targets
- You want armor penetration gameplay
- You want visual damage progression (steel → damaged → concrete → damaged → air)
- You want realistic military structures

### **Use Truly Hollow Shapes When:**
- You want fragile structures
- You want interior spaces
- You want bullets to pass through
- You want quick destruction

### **Use Solid Shapes When:**
- You want simple test objects
- You want maximum durability
- You don't need armor layers

---

## 📊 **Material Comparison**

| Material | ID | Color | Strength | Damage States |
|----------|----|----|----------|---------------|
| **Air** | 0 | Transparent | N/A | N/A |
| **Concrete** | 3 | Gray | Medium | Concrete → Damaged (crimson) → Air |
| **Steel** | 5 | Metallic | High | Steel → Damaged (orange) → Air |

---

## 💡 **Pro Tips**

### **Voxel Count Matters:**
- 32³ solid cube = 32,768 voxels
- 32³ hollow cube (2-voxel shell) = ~7,000 voxels
- **Hollow shapes render faster!**

### **Shell Thickness:**
- 1 voxel = Fragile, easy to penetrate
- 2 voxels = Balanced (default)
- 3+ voxels = Very durable, slow to destroy

### **Testing Shapes:**
1. Generate shape in Voxel Studio
2. Save to StreamingAssets
3. Create GameObject in Unity
4. Shoot it to see behavior!

---

## 🎯 **Next Steps**

Want to create custom shapes? You can:
1. **Modify existing generators** - Change sizes, materials, thicknesses
2. **Combine shapes** - Layer multiple shapes together
3. **Write new generators** - Create your own shape algorithms
4. **Hand-edit voxels** - Modify individual voxels in code

See `shape_generator.py` for implementation details!
