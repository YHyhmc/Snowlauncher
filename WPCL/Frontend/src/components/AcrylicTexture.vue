<template>
  <svg class="acrylic-texture" aria-hidden="true">
    <filter id="acrylic-grain" x="0" y="0" width="100%" height="100%">
      <feTurbulence type="fractalNoise" baseFrequency="0.72" numOctaves="4" seed="8" stitchTiles="stitch" />
      <feColorMatrix type="saturate" values="0" />
      <feComponentTransfer>
        <feFuncA type="table" tableValues="0 0.2" />
      </feComponentTransfer>
    </filter>
    <rect width="100%" height="100%" filter="url(#acrylic-grain)" />
  </svg>
</template>
