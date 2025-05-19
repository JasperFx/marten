export default {
    server: {
      fsServe: {
        root: '../' 
      }
    },
    optimizeDeps: {
      include: ['@braintree/sanitize-url'],
    },
    resolve: {
      alias: {
        dayjs: 'dayjs/',
      },
    },
    build: {
      chunkSizeWarningLimit: 3000
    }
  }
