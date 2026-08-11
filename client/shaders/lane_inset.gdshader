shader_type canvas_item;

// Inset/carved effect — modifies the existing rendered color to add edge shading
uniform vec3 highlight_tint : source_color = vec3(0.55, 0.50, 0.42);
uniform vec3 shadow_tint : source_color = vec3(0.02, 0.02, 0.02);
uniform float edge_width = 0.04;
uniform float highlight_strength = 0.25;
uniform float shadow_strength = 0.35;

void fragment() {
    vec4 base = COLOR;
    float d_top = UV.y;
    float d_left = UV.x;
    float d_bottom = 1.0 - UV.y;
    float d_right = 1.0 - UV.x;
    
    // Highlight on top and left edges
    float highlight = smoothstep(edge_width, 0.0, min(d_top, d_left));
    
    // Shadow at bottom and right (inside the carved hole)
    float shadow = smoothstep(edge_width, 0.0, min(d_bottom, d_right));
    
    vec4 result = base;
    result.rgb = mix(result.rgb, highlight_tint, highlight * highlight_strength);
    result.rgb = mix(result.rgb, shadow_tint, shadow * shadow_strength);
    
    COLOR = result;
}