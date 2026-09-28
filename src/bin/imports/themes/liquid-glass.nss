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
				[0, #ffffff, @if(sys.dark, 14, 45)],
				[0.35, #ffffff, @if(sys.dark, 4, 12)],
				[1, #ffffff, 0]
			]
		}
	}
	border
	{
		enabled=true
		size=1
		// bright rim, like light catching the glass edge
		color=@if(sys.dark, #ffffff, #ffffff)
		opacity=@if(sys.dark, 22, 70)
		radius=3
		padding=[4, 4, 6, 6]
	}
	shadow
	{
		enabled=true
		size=12
		color=#000000
		opacity=@if(sys.dark, 40, 18)
		offset=4
	}
	item
	{
		radius=3
		opacity=@if(sys.dark, 18, 55)
		padding=[8, 8, 4, 4]
		margin=[4, 4, 0, 0]
		text
		{
			normal=@if(sys.dark, #f5f5f7, #1d1d1f)
			select=@if(sys.dark, #ffffff, #000000)
			normal.disabled=@if(sys.dark, #8e8e93, #8e8e93)
			select.disabled=@if(sys.dark, #8e8e93, #8e8e93)
		}
		back
		{
			// glass "pill" behind the hovered item
			select=@if(sys.dark, #ffffff, #ffffff)
			select.disabled=@if(sys.dark, #3a3a3c, #e5e5ea)
		}
		border
		{
			select=@if(sys.dark, #ffffff, #ffffff)
		}
	}
	separator
	{
		size=1
		color=@if(sys.dark, #ffffff, #000000)
		opacity=@if(sys.dark, 14, 10)
		margin=[10, 10, 4, 4]
	}
	symbol
	{
		normal=@if(sys.dark, #f5f5f7, #1d1d1f)
		select=@if(sys.dark, #ffffff, #000000)
	}
	font
	{
		name="Segoe UI"
		size=13
	}
	image.align=2
}
