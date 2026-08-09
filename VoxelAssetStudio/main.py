# Steel Tide: Voxel Asset Studio
# main.py - Application entry point

import sys
import os
from PyQt6.QtWidgets import QApplication
from PyQt6.QtGui import QPalette, QColor
from PyQt6.QtCore import Qt
from voxel_editor import VoxelEditor

def set_dark_theme(app):
    """Apply dark theme (like Kalshi Dashboard!)"""
    app.setStyle('Fusion')
    
    dark_palette = QPalette()
    dark_palette.setColor(QPalette.ColorRole.Window, QColor(53, 53, 53))
    dark_palette.setColor(QPalette.ColorRole.WindowText, Qt.GlobalColor.white)
    dark_palette.setColor(QPalette.ColorRole.Base, QColor(25, 25, 25))
    dark_palette.setColor(QPalette.ColorRole.AlternateBase, QColor(53, 53, 53))
    dark_palette.setColor(QPalette.ColorRole.ToolTipBase, Qt.GlobalColor.white)
    dark_palette.setColor(QPalette.ColorRole.ToolTipText, Qt.GlobalColor.white)
    dark_palette.setColor(QPalette.ColorRole.Text, Qt.GlobalColor.white)
    dark_palette.setColor(QPalette.ColorRole.Button, QColor(53, 53, 53))
    dark_palette.setColor(QPalette.ColorRole.ButtonText, Qt.GlobalColor.white)
    dark_palette.setColor(QPalette.ColorRole.BrightText, Qt.GlobalColor.red)
    dark_palette.setColor(QPalette.ColorRole.Link, QColor(42, 130, 218))
    dark_palette.setColor(QPalette.ColorRole.Highlight, QColor(42, 130, 218))
    dark_palette.setColor(QPalette.ColorRole.HighlightedText, Qt.GlobalColor.black)
    
    app.setPalette(dark_palette)

def main():
    """Application entry point"""
    print("=" * 50)
    print("  Voxel Asset Studio - Steel Tide")
    print("=" * 50)
    print()
    
    app = QApplication(sys.argv)
    
    # Apply dark theme
    set_dark_theme(app)
    
    # Create main window
    editor = VoxelEditor()
    editor.showMaximized()
    
    # Start with blank canvas - no auto-loading
    print("📋 Starting with blank canvas")
    print("   Use Generate menu to create procedural shapes")
    
    print()
    print("✅ Application ready!")
    print()
    print("🖱️ Mouse Controls:")
    print("   - Left-click: Paint/interact with voxels ✅")
    print("   - Middle-click: Pan camera")
    print("   - Right-click: Orbit camera")
    print("   - Wheel: Zoom in/out")
    print()
    print("⌨️ Keyboard:")
    print("   - WASD: Pan camera")
    print("   - Q/E: Pan up/down")
    print()
    print("🎨 Try clicking on a voxel to paint!")
    print("⚠️ Note: Colors may appear uniform (OpenGL limitation)")
    print()
    
    sys.exit(app.exec())

if __name__ == "__main__":
    main()
