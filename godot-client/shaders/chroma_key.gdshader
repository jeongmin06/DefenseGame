shader_type canvas_item;

uniform float opacity : hint_range(0.0, 1.0) = 1.0;
uniform vec4 tint_color : source_color = vec4(1.0);
uniform float tint_strength : hint_range(0.0, 1.0) = 0.0;

void fragment() {
	vec4 source = texture(TEXTURE, UV);
	float cyan_strength = min(source.g, source.b) - source.r;
	float cyan_balance = 1.0 - smoothstep(0.08, 0.22, abs(source.g - source.b));
	float keyed = smoothstep(0.20, 0.48, cyan_strength) * cyan_balance;
	source.rgb = mix(source.rgb, tint_color.rgb, tint_strength);
	source.a *= (1.0 - keyed) * opacity;
	COLOR = source;
}
