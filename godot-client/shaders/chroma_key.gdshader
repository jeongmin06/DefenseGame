shader_type canvas_item;

void fragment() {
	vec4 source = texture(TEXTURE, UV);
	float cyan_strength = min(source.g, source.b) - source.r;
	float cyan_balance = 1.0 - smoothstep(0.08, 0.22, abs(source.g - source.b));
	float keyed = smoothstep(0.20, 0.48, cyan_strength) * cyan_balance;
	source.a *= 1.0 - keyed;
	COLOR = source;
}
