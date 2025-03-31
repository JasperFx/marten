<script setup>
import DefaultTheme from 'vitepress/theme'

const { Layout } = DefaultTheme
</script>

<template>
  <Layout>
    <template #aside-ads-before>
        <div class="info-box">
          <p>For any queries, join our <a class="vp-external-link-icon" href="https://discord.gg/WMxrvegf8H" target="_blank" rel="noreferrer">Discord Channel</a> to reach us faster.</p>
        </div>
        <div class="info-box">
          <img src="https://jasperfx.net/logo.png" alt="JasperFx Logo" />
          <p>JasperFx provides formal support for Marten and other JasperFx libraries. Please check our <a class="vp-external-link-icon" href="https://jasperfx.net/support-plans/" target="_blank" rel="noreferrer">Support Plans</a> for more details.</p>
        </div>
    </template>
  </Layout>
</template>

<style scoped>
    .info-box {
        border-top: 1px solid var(--vp-c-divider); margin:5px; padding: 5px;
    }

    .info-box a {
        color: var(--vp-c-brand-2);
    }
</style>