export default {
    server: {
      fsServe: {
        root: '../' 
      }
    },
    optimizeDeps: { include: ['moment-mini', '@braintree/sanitize-url', 'dagre', 'dagre-d3', 'graphlib'] }
  }