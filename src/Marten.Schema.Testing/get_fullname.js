// SAMPLE: get_fullname.js
module.exports = function (doc) {
    return {fullname: doc.FirstName + ' ' + doc.LastName};
}
// ENDSAMPLE