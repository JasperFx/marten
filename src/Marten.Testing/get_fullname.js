module.exports = function(doc) {
    return {fullname: doc.FirstName + ' ' + doc.LastName};
}