// SAMPLE: get_fullname.js
module.exports = function (doc) {
    return {fullname: doc.firstName + ' ' + doc.lastName};
}
// ENDSAMPLE