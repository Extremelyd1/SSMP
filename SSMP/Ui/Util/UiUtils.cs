using UnityEngine;

namespace SSMP.Ui.Util;

/// <summary>
/// Utility methods for UI components.
/// </summary>
public static class UiUtils {
    /// <summary>
    /// Creates a horizontal gradient texture (bright center, fade to edges).
    /// </summary>
    /// <param name="width">Width of the texture.</param>
    /// <param name="height">Height of the texture.</param>
    /// <returns>The gradient texture.</returns>
    public static Texture2D CreateHorizontalGradientTexture(int width, int height) {
        var texture = new Texture2D(width, height);
        var pixels = new Color[width * height];
        
        for (int x = 0; x < width; x++) {
            // Calculate alpha based on distance from center
            float distFromCenter = Mathf.Abs((x / (float)width) - 0.5f) * 2f; // 0 at center, 1 at edges
            float alpha = 1f - distFromCenter; // 1 at center, 0 at edges
            alpha = Mathf.Pow(alpha, 2f); // Sharper falloff
            
            Color pixelColor = new Color(1f, 1f, 1f, alpha);
            
            for (int y = 0; y < height; y++) {
                pixels[y * width + x] = pixelColor;
            }
        }
        
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    /// <summary>
    /// Creates a texture with rounded corners.
    /// </summary>
    /// <param name="width">The width of the texture.</param>
    /// <param name="height">The height of the texture.</param>
    /// <param name="radius">The corner radius.</param>
    /// <returns>The created texture.</returns>
    public static Texture2D CreateRoundedRectTexture(int width, int height, int radius) {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var pixels = new Color[width * height];
        
        // Pre-calculate radius squared for performance
        var radiusSq = radius * radius;
        var borderWidth = 6;
        var borderThreshold = radius - borderWidth;
        var borderThresholdSq = borderThreshold * borderThreshold;
        
        var fillColor = new Color(0.1f, 0.1f, 0.1f, 0.85f);
        
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                var index = y * width + x;
                
                // Check if in corner regions
                bool inCorner = false;
                float distSq = 0;
                
                if (x < radius && y >= height - radius) {
                    // Top-left
                    float dx = radius - x;
                    float dy = (height - radius) - y;
                    distSq = dx * dx + dy * dy;
                    inCorner = true;
                    if (distSq > radiusSq) {
                        pixels[index] = Color.clear;
                        continue;
                    }
                } else if (x >= width - radius && y >= height - radius) {
                    // Top-right
                    float dx = x - (width - radius);
                    float dy = (height - radius) - y;
                    distSq = dx * dx + dy * dy;
                    inCorner = true;
                    if (distSq > radiusSq) {
                        pixels[index] = Color.clear;
                        continue;
                    }
                } else if (x < radius && y < radius) {
                    // Bottom-left
                    float dx = radius - x;
                    float dy = radius - y;
                    distSq = dx * dx + dy * dy;
                    inCorner = true;
                    if (distSq > radiusSq) {
                        pixels[index] = Color.clear;
                        continue;
                    }
                } else if (x >= width - radius && y < radius) {
                    // Bottom-right
                    float dx = x - (width - radius);
                    float dy = radius - y;
                    distSq = dx * dx + dy * dy;
                    inCorner = true;
                    if (distSq > radiusSq) {
                        pixels[index] = Color.clear;
                        continue;
                    }
                }
                
                // Determine if border or fill
                bool isBorder = false;
                if (inCorner) {
                    isBorder = distSq > borderThresholdSq;
                } else {
                    isBorder = x < borderWidth || x >= width - borderWidth || 
                               y < borderWidth || y >= height - borderWidth;
                }
                
                pixels[index] = isBorder ? Color.black : fillColor;
            }
        }
        
        texture.SetPixels(pixels);
        texture.Apply();
        texture.filterMode = FilterMode.Bilinear;
        return texture;
    }
}
