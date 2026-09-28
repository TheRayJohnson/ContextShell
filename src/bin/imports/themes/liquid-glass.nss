// Liquid Glass: a translucent, frosted panel with a bright specular edge and a soft
// top sheen, in the spirit of Apple's Liquid Glass material. Follows Windows light/dark.
// The tint is kept fairly opaque so text stays readable on busy wallpapers.
// Tweak $glass_opacity (0-100): higher is more solid, lower is more see-through.
// Needs "Transparency effects" on in Windows Settings > Personalisation > Colours;
// without it Windows draws the same colors as a solid panel.

$glass_opacity = 78

theme
{
	name="modern"
	dark=auto
	background
	{
		color=@if(sys.dark, #1c1c1e, #f5f5f7)
		opacity=glass_opacity
		effect=3
		gradient
		{
			enabled=true
			// top-to-bottom specular sheen
			linear=[0, 0, 0, 100]
			stop=[
				[0, #ffffff, @if(sys.dark, 10, 40)],
				[0.3, #ffffff, @if(sys.dark, 3, 10)],
				[1, #ffffff, 0]
			]
		}
	}
	border
	{
		enabled=true
		size=1
		// bright rim, like light catching the glass edge
		color=#ffffff
		opacity=@if(sys.dark, 18, 70)
		radius=3
	}
	shadow
	{
		enabled=true
		size=8
		color=#000000
		opacity=@if(sys.dark, 35, 15)
		offset=3
	}
	item
	{
		radius=3
		text
		{
			normal=@if(sys.dark, #f5f5f7, #1d1d1f)
			select=@if(sys.dark, #ffffff, #000000)
			normal.disabled=#8e8e93
			select.disabled=#8e8e93
		}
		back
		{
			// Rows are clear so the glass shows through; only the hovered row gets a soft pill.
			normal=[#000000, 0]
			normal.disabled=[#000000, 0]
			select=@if(sys.dark, [#ffffff, 12], [#000000, 7])
			select.disabled=@if(sys.dark, [#ffffff, 5], [#000000, 3])
		}
	}
	separator
	{
		color=@if(sys.dark, #ffffff, #000000)
		opacity=@if(sys.dark, 12, 9)
	}
	symbol
	{
		normal=@if(sys.dark, #f5f5f7, #1d1d1f)
		select=@if(sys.dark, #ffffff, #000000)
	}
	font.name="Segoe UI"
	image.align=2
}
