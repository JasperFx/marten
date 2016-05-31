module.exports = function(doc) {
    return {UserName: (doc.FirstName + '.' + doc.LastName).toLowerCase()};
}