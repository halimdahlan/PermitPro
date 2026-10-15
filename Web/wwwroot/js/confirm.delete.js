$(document).ready(function () {

  $('#btnDeleteItem').click(function () {
    const companyId = $(this).data('company-id');
    const controller = $(this).data('controller');
    const entity = $(this).data('entity');
    const itemId = $(this).data('item-id');

    const dlgConfirmDeleteItem = new bootstrap.Modal('#dlgConfirmDeleteItem');
    dlgConfirmDeleteItem.hide();

    deleteItem(companyId, controller, entity, itemId);
  });


  $('#dlgConfirmDeleteItem').on('hide.bs.modal', event => {
  });

});

const confirmDelete = (id, company, controller, entity) => {
  const dlgConfirmDeleteItem = new bootstrap.Modal('#dlgConfirmDeleteItem');
  dlgConfirmDeleteItem.show();

  $('#btnDeleteItem').data('company-id', company);
  $('#btnDeleteItem').data('controller', controller);
  $('#btnDeleteItem').data('entity', entity);
  $('#btnDeleteItem').data('item-id', id);
};


const deleteItem = (companyId, controller, entity, itemId) => {
  if (!itemId) {
    console.log('Item ID is missing.');
    return;
  }

  $('.loading').show();

  const xhr = $.ajax({
    url: `${companyBase}/${controller}/${itemId}`,
    method: 'DELETE',
  });

  xhr.done((response, status) => {
    if (response.data == 'OK') {
      
    }
  });

  xhr.fail((response, status) => {
    alert(response.message);
  });

  xhr.always(() => {
    $('#btnDeleteItem').data('company-id', '');
    $('#btnDeleteItem').data('controller', '');
    $('#btnDeleteItem').data('entity', '');
    $('#btnDeleteItem').data('item-id', '');
    $('.loading').hide();
  });
};